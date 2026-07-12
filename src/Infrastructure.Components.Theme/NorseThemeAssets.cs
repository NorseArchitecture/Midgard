namespace Norse.Infrastructure.Components.Theme;

/// <summary>
/// Well-known static-asset paths for the platform's theme selection machinery. Every Yggdrasil
/// host links <see cref="StylesheetPath"/> once, in its own root document — this is the one
/// place that path string is allowed to exist.
/// </summary>
public static class NorseThemeAssets
{
	/// <summary>
	/// The static-asset path of the platform's plain-CSS design-token stylesheet, as published by
	/// <c>Norse.DesignSystem.Tokens</c>. Every host links this path once, in its own root document.
	/// </summary>
	public const string StylesheetPath = "_content/Norse.DesignSystem.Tokens/norse-design-tokens.css";
}
