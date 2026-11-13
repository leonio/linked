//! The settings page: theme, account and about.

use guit_core::settings::Theme;
use windows_reactor::*;

use crate::{Guit, Message};

pub fn page(theme: Theme, login: &str, context: &ViewContext<Guit>) -> View {
    let section = |title: &str| {
        TextBlock::new()
            .text(title)
            .font_weight(FontWeight::SEMI_BOLD)
            .margin(Thickness::new(0.0, 16.0, 0.0, 4.0))
    };

    ScrollViewer::new()
        .content(
            StackPanel::new()
                .max_width(720.0)
                .horizontal_alignment(HorizontalAlignment::Left)
                .margin(Thickness::new(24.0, 16.0, 24.0, 24.0))
                .spacing(8.0)
                .children((
                    TextBlock::new().text("Settings").font_size(28.0),
                    section("Appearance"),
                    // The items are in the order of Theme::ALL, so the selected index is the theme.
                    RadioButtons::new()
                        .header("Theme")
                        .items_source(Theme::ALL.map(Theme::label))
                        .selected_index(Theme::ALL.iter().position(|option| *option == theme))
                        .on_selection_changed(context.callback(Message::ThemeSelected)),
                    section("Account"),
                    StackPanel::new()
                        .orientation(Orientation::Horizontal)
                        .spacing(12.0)
                        .children((
                            TextBlock::new()
                                .text(if login.is_empty() { "Signed in" } else { login })
                                .vertical_alignment(VerticalAlignment::Center),
                            Button::new()
                                .content("Sign out")
                                .on_click(context.message(Message::SignOut)),
                        )),
                    section("About"),
                    TextBlock::new()
                        .text(format!(
                            "Guit {} (Rust, windows-reactor)",
                            env!("CARGO_PKG_VERSION")
                        ))
                        .opacity(0.7),
                    HyperlinkButton::new()
                        .content("Project on GitHub")
                        .on_click(context.message(Message::OpenProject)),
                )),
        )
        .into()
}
