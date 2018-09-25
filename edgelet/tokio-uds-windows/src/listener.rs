// Copyright (c) Microsoft. All rights reserved.

use std::io;
use std::path::Path;
use futures::Poll;
use {SocketAddr, UnixStream};

pub struct UnixListener;

impl UnixListener {
    pub fn bind<P>(_path: P) -> io::Result<UnixListener>
    where
        P: AsRef<Path>,
    {
        Ok(UnixListener)
    }

    pub fn poll_accept(&self) -> Poll<(UnixStream, SocketAddr), io::Error> {
        Ok((UnixStream, SocketAddr::new()).into())
    }
}