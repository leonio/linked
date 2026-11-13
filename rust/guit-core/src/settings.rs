//! Settings that survive restarts, and the keyboard shortcuts. No I/O here: the app reads and writes the file.
//!
//! The file is the C# app's: `%LOCALAPPDATA%\Guit\settings.json`, a flat JSON object of strings. Sharing it means
//! a theme chosen in one build applies to the other, as the sign-in already does.

use std::collections::BTreeMap;

const THEME: &str = "theme";

/// The window theme. Stored as "System", "Light" or "Dark", the C# `AppTheme` names.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum Theme {
    #[default]
    System,
    Light,
    Dark,
}

impl Theme {
    /// In the order the settings page lists them.
    pub const ALL: [Self; 3] = [Self::System, Self::Light, Self::Dark];

    pub fn label(self) -> &'static str {
        match self {
            Self::System => "Use Windows setting",
            Self::Light => "Light",
            Self::Dark => "Dark",
        }
    }

    fn name(self) -> &'static str {
        match self {
            Self::System => "System",
            Self::Light => "Light",
            Self::Dark => "Dark",
        }
    }
}

/// The settings file's contents. Unknown keys (written by the other build) are kept when saving.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct Settings {
    values: BTreeMap<String, String>,
}

impl Settings {
    /// Reads the file's text. A missing or damaged file means defaults; it never fails.
    pub fn parse(json: Option<&str>) -> Self {
        let values = json
            .and_then(|json| serde_json::from_str(json).ok())
            .unwrap_or_default();
        Self { values }
    }

    pub fn to_json(&self) -> String {
        serde_json::to_string(&self.values).unwrap_or_else(|_| "{}".to_string())
    }

    /// The saved theme; anything missing or unreadable means "follow Windows". Case does not matter, as in C#.
    pub fn theme(&self) -> Theme {
        let saved = self
            .values
            .get(THEME)
            .map(String::as_str)
            .unwrap_or_default();
        Theme::ALL
            .into_iter()
            .find(|theme| theme.name().eq_ignore_ascii_case(saved))
            .unwrap_or_default()
    }

    pub fn set_theme(&mut self, theme: Theme) {
        self.values
            .insert(THEME.to_string(), theme.name().to_string());
    }
}

/// What a key press asks for. The same keys as the C# app.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Shortcut {
    /// Ctrl+1, Ctrl+2, Ctrl+3: the sections in pane order.
    Section(usize),
    /// F5.
    Refresh,
    /// Alt+Left.
    Back,
    /// Esc. Pages with a text box ignore it, so typing is never interrupted.
    Escape,
}

impl Shortcut {
    const KEY_1: u32 = 0x31;
    const KEY_3: u32 = 0x33;
    const NUMPAD_1: u32 = 0x61;
    const NUMPAD_3: u32 = 0x63;
    const F5: u32 = 0x74;
    const LEFT: u32 = 0x25;
    const ESCAPE: u32 = 0x1B;

    /// Maps a Windows virtual-key code and the modifier keys held to a shortcut.
    pub fn from_key(key: u32, control: bool, alt: bool) -> Option<Self> {
        match (key, control, alt) {
            (Self::KEY_1..=Self::KEY_3, true, false) => {
                Some(Self::Section((key - Self::KEY_1) as usize))
            }
            (Self::NUMPAD_1..=Self::NUMPAD_3, true, false) => {
                Some(Self::Section((key - Self::NUMPAD_1) as usize))
            }
            (Self::F5, false, false) => Some(Self::Refresh),
            (Self::LEFT, false, true) => Some(Self::Back),
            (Self::ESCAPE, false, false) => Some(Self::Escape),
            _ => None,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_theme_falls_back_to_system() {
        assert_eq!(Settings::parse(None).theme(), Theme::System);
        assert_eq!(Settings::parse(Some("not json")).theme(), Theme::System);
        assert_eq!(
            Settings::parse(Some(r#"{"theme":"purple"}"#)).theme(),
            Theme::System
        );
        assert_eq!(
            Settings::parse(Some(r#"{"theme":"dark"}"#)).theme(),
            Theme::Dark
        );
        assert_eq!(
            Settings::parse(Some(r#"{"theme":"Light"}"#)).theme(),
            Theme::Light
        );
    }

    #[test]
    fn saving_keeps_keys_written_by_the_other_build() {
        let mut settings = Settings::parse(Some(r#"{"theme":"Light","window":"maximised"}"#));

        settings.set_theme(Theme::Dark);

        assert_eq!(
            settings.to_json(),
            r#"{"theme":"Dark","window":"maximised"}"#
        );
        assert_eq!(
            Settings::parse(Some(&settings.to_json())).theme(),
            Theme::Dark
        );
    }

    #[test]
    fn keys_map_to_shortcuts() {
        assert_eq!(
            Shortcut::from_key(0x31, true, false),
            Some(Shortcut::Section(0))
        );
        assert_eq!(
            Shortcut::from_key(0x33, true, false),
            Some(Shortcut::Section(2))
        );
        assert_eq!(
            Shortcut::from_key(0x62, true, false),
            Some(Shortcut::Section(1))
        );
        assert_eq!(
            Shortcut::from_key(0x74, false, false),
            Some(Shortcut::Refresh)
        );
        assert_eq!(Shortcut::from_key(0x25, false, true), Some(Shortcut::Back));
        assert_eq!(
            Shortcut::from_key(0x1B, false, false),
            Some(Shortcut::Escape)
        );

        // Plain digits and arrows are typing, not shortcuts.
        assert_eq!(Shortcut::from_key(0x31, false, false), None);
        assert_eq!(Shortcut::from_key(0x25, false, false), None);
        assert_eq!(Shortcut::from_key(0x34, true, false), None);
        assert_eq!(Shortcut::from_key(0x74, true, false), None);
    }
}
