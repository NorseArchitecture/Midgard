namespace Norse.Infrastructure.Components.Theme.Tests;

public sealed class NorseThemeAssetsTests
{
	[Fact]
	void StylesheetPath_PointsAtDesignSystemTokensStaticWebAsset()
	{
		NorseThemeAssets.StylesheetPath.ShouldBe("_content/Norse.DesignSystem.Tokens/norse-design-tokens.css");
	}
}
