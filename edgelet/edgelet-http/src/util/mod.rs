// Copyright (c) Microsoft. All rights reserved.

use std::fmt;
use std::io::{self, Read, Write};
use std::net::SocketAddr;
#[cfg(unix)]
use std::os::unix::net::SocketAddr as UnixSocketAddr;

use bytes::{Buf, BufMut};
use edgelet_core::pid::Pid;
use futures::Poll;
use tokio_io::{AsyncRead, AsyncWrite};
#[cfg(windows)]
use tokio_named_pipe::PipeStream;
use tokio_tcp::TcpStream;
#[cfg(unix)]
use tokio_uds::UnixStream;
#[cfg(windows)]
use tokio_uds_windows::{SocketAddr as UnixSocketAddr, UnixStream};

#[cfg(unix)]
use pid::UnixStreamExt;

pub mod connector;
mod hyperwrap;
pub mod incoming;
pub mod proxy;

pub use self::connector::UrlConnector;
pub use self::incoming::Incoming;

pub enum StreamSelector {
    Tcp(TcpStream),
    #[cfg(windows)]
    Pipe(PipeStream),
    Unix(UnixStream),
}

impl StreamSelector {
    pub fn pid(&self) -> io::Result<Pid> {
        match *self {
            StreamSelector::Tcp(_) => Ok(Pid::Any),
            #[cfg(windows)]
            StreamSelector::Pipe(_) => Ok(Pid::Any),
            StreamSelector::Unix(ref stream) => stream.pid(),
        }
    }
}

impl Read for StreamSelector {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        match *self {
            StreamSelector::Tcp(ref mut stream) => stream.read(buf),
            #[cfg(windows)]
            StreamSelector::Pipe(ref mut stream) => stream.read(buf),
            StreamSelector::Unix(ref mut stream) => stream.read(buf),
        }
    }
}

impl Write for StreamSelector {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        match *self {
            StreamSelector::Tcp(ref mut stream) => stream.write(buf),
            #[cfg(windows)]
            StreamSelector::Pipe(ref mut stream) => stream.write(buf),
            StreamSelector::Unix(ref mut stream) => stream.write(buf),
        }
    }

    fn flush(&mut self) -> io::Result<()> {
        match *self {
            StreamSelector::Tcp(ref mut stream) => stream.flush(),
            #[cfg(windows)]
            StreamSelector::Pipe(ref mut stream) => stream.flush(),
            StreamSelector::Unix(ref mut stream) => stream.flush(),
        }
    }
}

impl AsyncRead for StreamSelector {
    #[inline]
    unsafe fn prepare_uninitialized_buffer(&self, buf: &mut [u8]) -> bool {
        match *self {
            StreamSelector::Tcp(ref stream) => stream.prepare_uninitialized_buffer(buf),
            #[cfg(windows)]
            StreamSelector::Pipe(ref stream) => stream.prepare_uninitialized_buffer(buf),
            StreamSelector::Unix(ref stream) => stream.prepare_uninitialized_buffer(buf),
        }
    }

    #[inline]
    fn read_buf<B: BufMut>(&mut self, buf: &mut B) -> Poll<usize, io::Error> {
        match *self {
            StreamSelector::Tcp(ref mut stream) => stream.read_buf(buf),
            #[cfg(windows)]
            StreamSelector::Pipe(ref mut stream) => stream.read_buf(buf),
            StreamSelector::Unix(ref mut stream) => stream.read_buf(buf),
        }
    }
}

impl AsyncWrite for StreamSelector {
    fn shutdown(&mut self) -> Poll<(), io::Error> {
        match *self {
            StreamSelector::Tcp(ref mut stream) => <&TcpStream>::shutdown(&mut &*stream),
            #[cfg(windows)]
            StreamSelector::Pipe(ref mut stream) => PipeStream::shutdown(stream),
            StreamSelector::Unix(ref mut stream) => <&UnixStream>::shutdown(&mut &*stream),
        }
    }

    #[inline]
    fn write_buf<B: Buf>(&mut self, buf: &mut B) -> Poll<usize, io::Error> {
        match *self {
            StreamSelector::Tcp(ref mut stream) => stream.write_buf(buf),
            #[cfg(windows)]
            StreamSelector::Pipe(ref mut stream) => stream.write_buf(buf),
            StreamSelector::Unix(ref mut stream) => stream.write_buf(buf),
        }
    }
}

pub enum IncomingSocketAddr {
    Tcp(SocketAddr),
    Unix(UnixSocketAddr),
}

impl fmt::Display for IncomingSocketAddr {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        match *self {
            IncomingSocketAddr::Tcp(ref socket) => socket.fmt(f),
            IncomingSocketAddr::Unix(ref socket) => {
                if let Some(path) = socket.as_pathname() {
                    write!(f, "{}", path.display())
                } else {
                    write!(f, "unknown")
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_pid() {
        let (a, b) = UnixStream::pair().unwrap();
        assert_eq!(a.pid().unwrap(), b.pid().unwrap());
        match a.pid().unwrap() {
            Pid::None => panic!("no pid 'a'"),
            Pid::Any => panic!("any pid 'a'"),
            Pid::Value(_) => (),
        }
        match b.pid().unwrap() {
            Pid::None => panic!("no pid 'b'"),
            Pid::Any => panic!("any pid 'b'"),
            Pid::Value(_) => (),
        }
    }
}
