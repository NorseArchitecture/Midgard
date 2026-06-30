using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Infrastructure;

/// <summary>
/// Extension methods for <see cref="IHostApplicationBuilder"/> to register Norse migrations infrastructure.
/// </summary>
public static class HostApplicationBuilderExtensions
{
	/// <summary>
	/// Registers <see cref="MigrationRunnerService"/> as a hosted service that runs all
	/// <see cref="Norse.Abstractions.Migrations.IMigrationContributor"/> implementations on startup and stops the application on completion.
	/// </summary>
	/// <param name="builder">The host application builder.</param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorseMigrationsRunner(this IHostApplicationBuilder builder)
	{
		builder.Services.AddHostedService<MigrationRunnerService>();
		return builder;
	}
}
