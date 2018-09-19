// Copyright (c) Microsoft. All rights reserved.

use std::io;

use futures::{Poll, Stream};
use tokio_tcp::TcpListener;
#[cfg(unix)]
use tokio_uds::UnixListener;

use util::{IncomingSocketAddr, StreamSelector};

pub enum Incoming {
    Tcp(TcpListener),
    #[cfg(unix)]
    Unix(UnixListener),
}

impl Stream for Incoming {
    type Item = (StreamSelector, IncomingSocketAddr);
    type Error = io::Error;

    fn poll(&mut self) -> Poll<Option<Self::Item>, Self::Error> {
        Ok(match *self {
            Incoming::Tcp(ref mut listener) => {
                try_nb!(listener.poll_accept()).map(|(stream, addr)| Some((
                    StreamSelector::Tcp(stream),
                    IncomingSocketAddr::Tcp(addr),
                )))
            }
            #[cfg(unix)]
            Incoming::Unix(ref mut listener) => {
                try_nb!(listener.poll_accept()).map(|(stream, addr)| Some((
                    StreamSelector::Unix(stream),
                    IncomingSocketAddr::Unix(addr),
                )))
            }
        })
    }
}
