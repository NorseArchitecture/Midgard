using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Norse.Infrastructure.Components.Theme.FluentUI.Tests;

public sealed class ServiceCollectionExtensionsTests
{
	[Fact]
	void AddNorseFluentUiTheme_RegistersIThemeService()
	{
		var services = new ServiceCollection();

		services.AddNorseFluentUiTheme();

		services.ShouldContain(d => d.ServiceType == typeof(IThemeService));
	}
}
