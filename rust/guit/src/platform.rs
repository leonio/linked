//! Everything that touches Windows outside the UI library: HTTP, the credential locker,
//! the browser and the clipboard. The same WinRT APIs the C# app's platform services use.

use std::sync::OnceLock;

use guit_core::http::{Request, Response, Transport};
use windows::ApplicationModel::DataTransfer::{Clipboard, DataPackage};
use windows::Foundation::Uri;
use windows::Security::Credentials::{PasswordCredential, PasswordVault};
use windows::Storage::Streams::UnicodeEncoding;
use windows::System::Launcher;
use windows::Web::Http::{HttpClient, HttpMethod, HttpRequestMessage, HttpStringContent};
use windows::Win32::{CO_MTA_USAGE_COOKIE, CoIncrementMTAUsage};
use windows::core::{HSTRING, Result, h};

// The same credential the C# app stores, so signing in with either build signs in both.
const VAULT_RESOURCE: &HSTRING = h!("Guit");
const VAULT_USER: &HSTRING = h!("github");

/// Call once at start-up. Keeps a multithreaded COM apartment alive for the process, so the
/// background threads that make HTTP calls can use WinRT without initialising COM themselves.
pub fn initialize() {
    let mut cookie = CO_MTA_USAGE_COOKIE::default();
    // SAFETY: `cookie` is a valid place for the result; it is deliberately never released.
    _ = unsafe { CoIncrementMTAUsage(&mut cookie) };
}

/// HTTP over `Windows.Web.Http`. One client for the process, so connections are reused.
pub struct WinRtTransport;

impl WinRtTransport {
    fn client() -> Result<&'static HttpClient> {
        static CLIENT: OnceLock<HttpClient> = OnceLock::new();
        if let Some(client) = CLIENT.get() {
            return Ok(client);
        }

        let client = HttpClient::new()?;
        Ok(CLIENT.get_or_init(|| client))
    }

    fn try_send(request: &Request) -> Result<Response> {
        let method = HttpMethod::Create(&HSTRING::from(request.method))?;
        let message =
            HttpRequestMessage::Create(&method, &Uri::CreateUri(&HSTRING::from(&request.url))?)?;

        let headers = message.Headers()?;
        // GitHub rejects requests without a User-Agent.
        headers.TryAppendWithoutValidation(h!("User-Agent"), h!("Guit-rs/0.1"))?;
        for (name, value) in &request.headers {
            headers.TryAppendWithoutValidation(&HSTRING::from(*name), &HSTRING::from(value))?;
        }

        if let Some((content_type, body)) = &request.body {
            let content = HttpStringContent::CreateFromStringWithEncodingAndMediaType(
                &HSTRING::from(body),
                UnicodeEncoding::Utf8,
                &HSTRING::from(*content_type),
            )?;
            message.SetContent(&content)?;
        }

        let reply = Self::client()?.SendRequestAsync(&message)?.join()?;
        let mut reply_headers = Vec::new();
        for pair in reply.Headers()? {
            reply_headers.push((
                pair.Key()?.to_string_lossy().to_ascii_lowercase(),
                pair.Value()?.to_string_lossy(),
            ));
        }

        Ok(Response {
            status: reply.StatusCode()?.0 as u16,
            headers: reply_headers,
            body: reply
                .Content()?
                .ReadAsStringAsync()?
                .join()?
                .to_string_lossy(),
        })
    }
}

impl Transport for WinRtTransport {
    fn send(&self, request: &Request) -> std::result::Result<Response, String> {
        Self::try_send(request).map_err(|error| error.message())
    }
}

/// The access token kept in the Windows Credential Locker, if there is one.
pub fn load_token() -> Option<String> {
    let credential = PasswordVault::new()
        .ok()?
        .Retrieve(VAULT_RESOURCE, VAULT_USER)
        .ok()?;
    credential.RetrievePassword().ok()?;
    credential
        .Password()
        .ok()
        .map(|password| password.to_string_lossy())
        .filter(|token| !token.is_empty())
}

pub fn save_token(token: &str) {
    _ = (|| -> Result<()> {
        let credential = PasswordCredential::CreatePasswordCredential(
            VAULT_RESOURCE,
            VAULT_USER,
            &HSTRING::from(token),
        )?;
        PasswordVault::new()?.Add(&credential)
    })();
}

pub fn clear_token() {
    _ = (|| -> Result<()> {
        let vault = PasswordVault::new()?;
        vault.Remove(&vault.Retrieve(VAULT_RESOURCE, VAULT_USER)?)
    })();
}

/// Opens a URL in the default browser. Failures are ignored: there is nothing useful to do about them.
pub fn open_in_browser(url: &str) {
    _ = (|| -> Result<()> {
        Launcher::LaunchUriAsync(&Uri::CreateUri(&HSTRING::from(url))?)?;
        Ok(())
    })();
}

/// Puts text on the clipboard. Call from the UI thread.
pub fn copy_to_clipboard(text: &str) {
    _ = (|| -> Result<()> {
        let package = DataPackage::new()?;
        package.SetText(&HSTRING::from(text))?;
        Clipboard::SetContent(&package)
    })();
}

/// The settings file the C# app also uses: `%LOCALAPPDATA%\Guit\settings.json`.
fn settings_path() -> Option<std::path::PathBuf> {
    Some(
        std::path::PathBuf::from(std::env::var_os("LOCALAPPDATA")?)
            .join("Guit")
            .join("settings.json"),
    )
}

/// The settings file's text, if there is one.
pub fn load_settings() -> Option<String> {
    std::fs::read_to_string(settings_path()?).ok()
}

/// Writes the settings file. Failures are ignored: the setting still applies for this run.
pub fn save_settings(json: &str) {
    if let Some(path) = settings_path() {
        if let Some(folder) = path.parent() {
            _ = std::fs::create_dir_all(folder);
        }
        _ = std::fs::write(path, json);
    }
}
