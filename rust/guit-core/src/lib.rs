//! GitHub API client and view logic for Guit, the Rust side.
//!
//! This crate mirrors `Guit.Api` and `Guit.Core` of the C# app: everything that is not UI.
//! It has no Windows dependency, so `cargo test -p guit-core` runs on any machine. The app crate
//! supplies the one platform piece it needs, an implementation of [`http::Transport`].

pub mod actions;
pub mod api;
pub mod conversation;
pub mod device_flow;
pub mod diff;
pub mod http;
pub mod inbox;
pub mod link;
pub mod rich;
pub mod time;

/// The OAuth App client id. Public, not a secret; the same app the C# client signs in with.
pub const GITHUB_CLIENT_ID: &str = "Ov23lixGb2d0Qd0Wo2f3";

/// OAuth scopes, space separated. Same as the C# client.
pub const GITHUB_SCOPES: &str = "notifications repo read:user";
