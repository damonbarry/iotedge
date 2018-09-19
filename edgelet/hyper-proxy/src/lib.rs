// Copyright (c) Microsoft. All rights reserved.

/*!
 * A hyper Connector that proxies requests through a specified URL.
 *
 * Derived from https://github.com/tafia/hyper-proxy and https://github.com/seanmonstar/reqwest
 *
 * Replace this with https://github.com/tafia/hyper-proxy when it supports hyper 0.12
 * ( https://github.com/tafia/hyper-proxy/pull/5 )
 */

#![deny(warnings)]

extern crate bytes;
#[macro_use]
extern crate futures;
extern crate http;
extern crate hyper;
extern crate native_tls;
extern crate tokio_io;
extern crate tokio_tls;

use bytes::{Buf, BufMut, IntoBuf};
use futures::{Async, Future, Poll};

#[derive(Debug)]
pub enum Intercept {
    All,
}

#[derive(Debug)]
pub struct Proxy {
    uri: hyper::Uri,
}

impl Proxy {
    pub fn new(_intercept: Intercept, uri: hyper::Uri) -> Self {
        Proxy {
            uri,
        }
    }
}

pub struct ProxyConnector<C> {
    connector: C,
    proxy: Proxy,
    tls: native_tls::TlsConnector,
}

impl<C> ProxyConnector<C> {
    pub fn from_proxy(connector: C, proxy: Proxy) -> Result<Self, std::io::Error> {
        let tls =
            native_tls::TlsConnector::builder()
            .build()
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e))?;

        Ok(ProxyConnector {
            connector,
            proxy,
            tls,
        })
    }
}

impl<C> hyper::client::connect::Connect for ProxyConnector<C> where
    C: hyper::client::connect::Connect,
    <C as hyper::client::connect::Connect>::Future: 'static,
{
    type Transport = ProxyStream<<C as hyper::client::connect::Connect>::Transport>;
    type Error = std::io::Error;
    type Future = Box<Future<Item = (Self::Transport, hyper::client::connect::Connected), Error = Self::Error> + Send>;

    fn connect(&self, dst: hyper::client::connect::Destination) -> Self::Future {
        let mut proxy_dst = dst.clone();

        let new_scheme = self.proxy.uri.scheme_part().map_or("http", http::uri::Scheme::as_str);

        proxy_dst
        .set_scheme(new_scheme)
        .expect("proxy target scheme should be valid");

        proxy_dst
        .set_host(self.proxy.uri.host().expect("proxy target should have host"))
        .expect("proxy target host should be valid");

        proxy_dst.set_port(self.proxy.uri.port());

        if dst.scheme() == "https" {
            let host = dst.host().to_owned();
            let port = dst.port().unwrap_or(443);
            let tls = tokio_tls::TlsConnector::from(self.tls.clone());

            Box::new(
                self.connector.connect(proxy_dst)
                .map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e))
                .and_then(move |(conn, connected)| {
                    tunnel(conn, &host, port)
                    .and_then(move |tunneled| {
                        tls.connect(&host, tunneled)
                        .map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e))
                    })
                    .map(|io| (ProxyStream::Secured(io), connected.proxy(true)))
                })
            )
        }
        else {
            Box::new(
                self.connector.connect(proxy_dst)
                .then(|result| match result {
                    Ok((io, connected)) => Ok((ProxyStream::Regular(io), connected.proxy(true))),
                    Err(e) => Err(std::io::Error::new(std::io::ErrorKind::Other, e)),
                })
            )
        }
    }
}

pub enum ProxyStream<T> {
    Regular(T),
    Secured(tokio_tls::TlsStream<T>),
}

impl<T> std::io::Read for ProxyStream<T> where T: std::io::Read + std::io::Write {
    fn read(&mut self, buf: &mut [u8]) -> std::io::Result<usize> {
        match self {
            ProxyStream::Regular(s) => s.read(buf),
            ProxyStream::Secured(s) => s.read(buf),
        }
    }
}

impl<T> std::io::Write for ProxyStream<T> where T: std::io::Read + std::io::Write {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        match self {
            ProxyStream::Regular(s) => s.write(buf),
            ProxyStream::Secured(s) => s.write(buf),
        }
    }

    fn flush(&mut self) -> std::io::Result<()> {
        match self {
            ProxyStream::Regular(s) => s.flush(),
            ProxyStream::Secured(s) => s.flush(),
        }
    }
}

impl<T> tokio_io::AsyncRead for ProxyStream<T> where T: tokio_io::AsyncRead + tokio_io::AsyncWrite {
    unsafe fn prepare_uninitialized_buffer(&self, buf: &mut [u8]) -> bool {
        match self {
            ProxyStream::Regular(ref s) => s.prepare_uninitialized_buffer(buf),
            ProxyStream::Secured(ref s) => s.prepare_uninitialized_buffer(buf),
        }
    }

    fn read_buf<B: BufMut>(&mut self, buf: &mut B) -> Poll<usize, std::io::Error> {
        match self {
            ProxyStream::Regular(s) => s.read_buf(buf),
            ProxyStream::Secured(s) => s.read_buf(buf),
        }
    }
}

impl<T> tokio_io::AsyncWrite for ProxyStream<T> where T: tokio_io::AsyncRead + tokio_io::AsyncWrite {
    fn shutdown(&mut self) -> Poll<(), std::io::Error> {
        match self {
            ProxyStream::Regular(s) => s.shutdown(),
            ProxyStream::Secured(s) => s.shutdown(),
        }
    }

    fn write_buf<B: Buf>(&mut self, buf: &mut B) -> Poll<usize, std::io::Error> {
        match self {
            ProxyStream::Regular(s) => s.write_buf(buf),
            ProxyStream::Secured(s) => s.write_buf(buf),
        }
    }
}

fn tunnel<T>(conn: T, host: &str, port: u16) -> Tunnel<T> {
    let buf = format!("\
        CONNECT {0}:{1} HTTP/1.1\r\n\
        Host: {0}:{1}\r\n\
        \r\n\
    ", host, port);

    Tunnel {
        buf: buf.into_bytes().into_buf(),
        conn: Some(conn),
        state: TunnelState::Writing,
    }
}

#[derive(Debug)]
struct Tunnel<T> {
    buf: std::io::Cursor<Vec<u8>>,
    conn: Option<T>,
    state: TunnelState,
}

#[derive(Debug, Clone, Copy)]
enum TunnelState {
    Writing,
    Reading,
}

impl<T> Future for Tunnel<T> where T: tokio_io::AsyncRead + tokio_io::AsyncWrite {
    type Item = T;
    type Error = std::io::Error;

    fn poll(&mut self) -> Poll<Self::Item, Self::Error> {
        loop {
            match self.state {
                TunnelState::Writing => {
                    let n = try_ready!(self.conn.as_mut().unwrap().write_buf(&mut self.buf));
                    if !self.buf.has_remaining_mut() {
                        self.state = TunnelState::Reading;
                        self.buf.get_mut().truncate(0);
                    }
                    else if n == 0 {
                        return Err(tunnel_eof());
                    }
                },

                TunnelState::Reading => {
                    let n = try_ready!(self.conn.as_mut().unwrap().read_buf(&mut self.buf.get_mut()));
                    let read = &self.buf.get_ref()[..];
                    if n == 0 {
                        return Err(tunnel_eof());
                    }
                    else if read.len() > 12 {
                        if read.starts_with(b"HTTP/1.1 200") || read.starts_with(b"HTTP/1.0 200") {
                            if read.ends_with(b"\r\n\r\n") {
                                return Ok(Async::Ready(self.conn.take().unwrap()));
                            }
                            // else read more
                        } else {
                            return Err(std::io::Error::new(std::io::ErrorKind::Other, "unsuccessful tunnel"));
                        }
                    }
                },
            }
        }
    }
}

fn tunnel_eof() -> std::io::Error {
    std::io::Error::new(std::io::ErrorKind::UnexpectedEof, "unexpected eof while tunneling")
}
