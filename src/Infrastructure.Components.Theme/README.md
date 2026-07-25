# Norse.Infrastructure.Components.Theme

The plain-CSS half of the platform's theme selection machinery. No third-party UI-library dependency — every headless component (Asgard's `Loader`, any headless markup in a realm's `.Components` project) implicitly depends on this via `currentColor`, without ever referencing it directly. Consumes Naglfar's generated `Norse.DesignSystem.Tokens` package (`NorseThemeAssets`). See `Infrastructure.Components.Theme.FluentUI` for the FluentUI-specific sibling.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
