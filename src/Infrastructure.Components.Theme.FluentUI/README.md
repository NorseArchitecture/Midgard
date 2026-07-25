# Norse.Infrastructure.Components.Theme.FluentUI

Bootstraps FluentUI Blazor's theme from Naglfar's generated token seed. `AddNorseFluentUiTheme()` registers FluentUI's services; `NorseFluentDesignTheme` calls `IThemeService.SetThemeAsync` with `Mode="System"` and the seeded accent color. The only project in the `Infrastructure.Components.Theme` pair that references FluentUI — rides on the headless `Infrastructure.Components.Theme` as a `ProjectReference`.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
