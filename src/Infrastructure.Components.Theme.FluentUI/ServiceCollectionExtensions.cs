using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Norse.Infrastructure.Components.Theme.FluentUI;

/// <summary>
/// Registration entry point for the platform's FluentUI-backed theme selection machinery.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers FluentUI Blazor's component services, including <see cref="IThemeService"/>, which
	/// <see cref="NorseFluentDesignTheme"/> calls to bootstrap the platform's design tokens.
	/// </summary>
	public static IServiceCollection AddNorseFluentUiTheme(this IServiceCollection services) =>
		services.AddFluentUIComponents();
}
