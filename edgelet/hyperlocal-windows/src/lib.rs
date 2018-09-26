//! Hyper client bindings for unix domain sockets on Windows

extern crate futures;
extern crate hex;
extern crate hyper;
extern crate tokio_uds_windows;

use std::borrow::Cow;
use std::io;
use std::path::Path;
use futures::{Async, Future, Poll};
use hex::FromHex;
use hyper::client::connect::{Connect, Connected, Destination};
use hyper::Uri as HyperUri;
use tokio_uds_windows::{ConnectFuture as StreamConnectFuture, UnixStream};

const UNIX_SCHEME: &str = "unix";

/// A type which implements `Into` for hyper's  `hyper::Uri` type
/// targetting unix domain sockets.
///
/// You can use this with any of
/// the HTTP factory methods on hyper's Client interface
/// and for creating requests
///
/// ```no_run
/// extern crate hyper;
/// extern crate hyperlocal;
///
/// let url: hyper::Uri = hyperlocal::Uri::new(
///   "/path/to/socket", "/urlpath?key=value"
///  ).into();
///  let req = hyper::Request::get(url).body(()).unwrap();
/// ```
#[derive(Debug)]
pub struct Uri<'a> {
    /// url path including leading slash, path, and query string
    encoded: Cow<'a, str>,
}

impl<'a> Into<HyperUri> for Uri<'a> {
    fn into(self) -> HyperUri {
        self.encoded.as_ref().parse().unwrap()
    }
}

impl<'a> Uri<'a> {
    /// Productes a new `Uri` from path to domain socket and request path.
    /// request path should include a leading slash
    pub fn new<P>(socket: P, path: &'a str) -> Self
    where
        P: AsRef<Path>,
    {
        let host = hex::encode(socket.as_ref().to_string_lossy().as_bytes());
        let host_str = format!("unix://{}:0{}", host, path);
        Uri {
            encoded: Cow::Owned(host_str),
        }
    }

    // fixme: would like to just use hyper::Result and hyper::error::UriError here
    // but UriError its not exposed for external use
    fn socket_path(uri: &HyperUri) -> Option<String> {
        uri.host()
            .iter()
            .filter_map(|host| {
                Vec::from_hex(host)
                    .ok()
                    .map(|raw| String::from_utf8_lossy(&raw).into_owned())
            })
            .next()
    }

    fn socket_path_dest(dest: &hyper::client::connect::Destination) -> Option<String> {
        format!("unix://{}", dest.host())
            .parse()
            .ok()
            .and_then(|uri| Self::socket_path(&uri))
    }
}

/// A type which implements hyper's client connector interface
/// for unix domain sockets
///
/// `UnixConnector` instances expects uri's
/// to be constructued with `hyperlocal::Uri::new()` which produce uris with a `unix://`
/// scheme
///
/// # examples
///
/// ```no_run
/// extern crate hyper;
/// extern crate hyperlocal;
///
/// let client = hyper::Client::builder()
///    .build::<_, hyper::Body>(hyperlocal::UnixConnector::new());
/// ```
#[derive(Clone)]
pub struct UnixConnector;

impl UnixConnector {
    pub fn new() -> Self {
        UnixConnector
    }
}

impl Connect for UnixConnector {
    type Transport = UnixStream;
    type Error = io::Error;
    type Future = ConnectFuture;

    fn connect(&self, destination: Destination) -> Self::Future {
        ConnectFuture::Start(destination)
    }
}

pub enum ConnectFuture {
    Start(Destination),
    Connect(StreamConnectFuture),
}

impl Future for ConnectFuture {
    type Item = (UnixStream, Connected);
    type Error = io::Error;

    fn poll(&mut self) -> Poll<Self::Item, Self::Error> {
        loop {
            let next_state = match self {
                ConnectFuture::Start(destination) => {
                    if destination.scheme() != UNIX_SCHEME {
                        return Err(io::Error::new(
                            io::ErrorKind::InvalidInput,
                            format!("Invalid uri {:?}", destination),
                        ));
                    }

                    let path = match Uri::socket_path_dest(&destination) {
                        Some(path) => path,

                        None => {
                            return Err(io::Error::new(
                                io::ErrorKind::InvalidInput,
                                format!("Invalid uri {:?}", destination),
                            ))
                        }
                    };

                    ConnectFuture::Connect(UnixStream::connect(&path))
                }

                ConnectFuture::Connect(f) => match f.poll() {
                    Ok(Async::Ready(stream)) => return Ok(Async::Ready((stream, Connected::new()))),
                    Ok(Async::NotReady) => return Ok(Async::NotReady),
                    Err(err) => return Err(err),
                },
            };

            *self = next_state;
        }
    }
}