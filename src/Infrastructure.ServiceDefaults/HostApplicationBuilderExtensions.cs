using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Norse.Infrastructure.ServiceDefaults;

/// <summary>
/// Extension methods for <see cref="IHostApplicationBuilder"/> composing the shared observability
/// root — the one surface every container calls regardless of host shape.
/// </summary>
public static class HostApplicationBuilderExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		/// Registers health-check services with the <c>self</c> liveness check (tagged <c>live</c>) —
		/// the host-neutral registration rail later layers hang checks on. No reporter is registered
		/// here: web hosts map endpoints in the ASP.NET layer, the worker's publisher arrives with the
		/// messaging layer, and the migrations service never participates (its exit code is the contract).
		/// </summary>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddDefaultHealthChecks()
		{
			builder.Services
				.AddHealthChecks()
				.AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);
			return builder;
		}
	}
}
