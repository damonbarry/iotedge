// Copyright (c) Microsoft. All rights reserved.

#![cfg(windows)]

use std::path::Path;
use error::Error;
use tokio_uds_windows::UnixListener;
use util::incoming::Incoming;

pub fn listener<P: AsRef<Path>>(path: P) -> Result<Incoming, Error> {
    let listener = UnixListener::bind(path)?;
    Ok(Incoming::Unix(listener))
}

#[cfg(test)]
mod tests {
    use super::*;

    // TODO
}
