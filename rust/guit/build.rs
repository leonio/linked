//! Stages a private copy of the Windows App Runtime beside the executable (a self-contained build,
//! like the C# app's `WindowsAppSDKSelfContained`), so both apps ship the same way and neither
//! needs anything installed. Staging uses Windows tools, so it only runs when building on Windows;
//! `cargo check --target x86_64-pc-windows-msvc` from another OS still type-checks everything.
fn main() {
    #[cfg(windows)]
    windows_reactor_setup::as_self_contained();
}
