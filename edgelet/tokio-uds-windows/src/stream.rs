// Copyright (c) Microsoft. All rights reserved.

use std::io::{self, Read, Write};
use std::path::Path;
use edgelet_core::pid::Pid;
use futures::{Async, Future, Poll};
use tokio_io::{AsyncRead, AsyncWrite};

/// A structure representing a connected unix socket.
///
/// This socket can be connected directly with `UnixStream::connect` or accepted
/// from a listener with `UnixListener::incoming`.
/// TODO: Additionally, a pair of
/// anonymous Unix sockets can be created with `UnixStream::pair`.
#[derive(Debug)]
pub struct UnixStream;

#[derive(Debug)]
pub struct ConnectFuture {
    inner: State,
}

#[derive(Debug)]
enum State {
    Waiting(UnixStream),
    Error(io::Error),
    Empty,
}

impl UnixStream {
    pub fn connect<P>(_path: P) -> ConnectFuture
    where
        P: AsRef<Path>,
    {
        let res = Ok(UnixStream);

        let inner = match res {
            Ok(stream) => State::Waiting(stream),
            Err(e) => State::Error(e),
        };

        ConnectFuture { inner }
    }

    pub fn pid(&self) -> io::Result<Pid> {
        Ok(Pid::Value(1))
    }
}

impl Read for UnixStream {
    fn read(&mut self, _buf: &mut [u8]) -> io::Result<usize> {
        Ok(0)
    }
}

impl Write for UnixStream {
    fn write(&mut self, _buf: &[u8]) -> io::Result<usize> {
        Ok(0)
    }

    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

impl AsyncRead for UnixStream {
    unsafe fn prepare_uninitialized_buffer(&self, _: &mut [u8]) -> bool {
        false
    }
}

impl AsyncWrite for UnixStream {
    fn shutdown(&mut self) -> Poll<(), io::Error> {
        Ok(().into())
    }
}

impl<'a> Write for &'a UnixStream {
    fn write(&mut self, _buf: &[u8]) -> io::Result<usize> {
        Ok(1)
    }

    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

impl<'a> AsyncWrite for &'a UnixStream {
    fn shutdown(&mut self) -> Poll<(), io::Error> {
        Ok(().into())
    }
}

impl Future for ConnectFuture {
    type Item = UnixStream;
    type Error = io::Error;

    fn poll(&mut self) -> Poll<UnixStream, io::Error> {
        use std::mem;

        match self.inner {
            State::Waiting(ref mut _stream) => {
                // if let Async::NotReady = stream.io.poll_write_ready()? {
                //     return Ok(Async::NotReady)
                // }

                // if let Some(e) = try!(stream.io.get_ref().take_error()) {
                //     return Err(e)
                // }
            }
            State::Error(_) => {
                let e = match mem::replace(&mut self.inner, State::Empty) {
                    State::Error(e) => e,
                    _ => unreachable!(),
                };

                return Err(e)
            },
            State::Empty => panic!("can't poll stream twice"),
        }

        match mem::replace(&mut self.inner, State::Empty) {
            State::Waiting(stream) => Ok(Async::Ready(stream)),
            _ => unreachable!(),
        }
    }
}
