// Copyright (c) Microsoft. All rights reserved.

#![cfg(windows)]

extern crate edgelet_core;
extern crate futures;
extern crate tokio_io;
extern crate winapi;

use std::ffi::CStr;
use std::os::raw::c_char;
use std::path::Path;
use winapi::shared::ws2def::{ADDRESS_FAMILY, AF_UNIX};
use winapi::shared::ntdef::CHAR;

mod listener;
mod stream;

#[allow(non_camel_case_types)]
#[derive(Clone)]
struct sockaddr_un {
    pub sun_family: ADDRESS_FAMILY,
    pub sun_path: [CHAR; 108],
}

#[allow(non_camel_case_types)]
type socklen_t = u32;

#[derive(Clone)]
pub struct SocketAddr {
    addr: sockaddr_un,
    len: socklen_t,
}

impl SocketAddr {
    pub fn new() -> SocketAddr {
        SocketAddr {
            addr: sockaddr_un {
                sun_family: AF_UNIX as u16,
                sun_path: [0; 108],
            },
            len: 0,
        }
    }

    pub fn as_pathname(&self) -> Option<&Path> {
        let path = unsafe {
            CStr::from_ptr(&self.addr.sun_path as *const c_char)
        };
        match path.to_str() {
            Ok(p) => Some(Path::new(p)),
            Err(_) => None,
        }
    }
}

pub use listener::UnixListener;
pub use stream::UnixStream;
pub use stream::ConnectFuture;