// Copyright (c) Microsoft. All rights reserved.

use std::io::{self, Read, Write};
use edgelet_core::pid::Pid;
use futures::Poll;
use tokio_io::{AsyncRead, AsyncWrite};

pub struct UnixStream;

impl UnixStream {
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
