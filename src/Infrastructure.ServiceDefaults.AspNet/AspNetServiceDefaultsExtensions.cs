using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
///     The ASP.NET observability root. Each ASP.NET host calls exactly one of these — they compose
///     <c>AddServiceDefaults()</c> rather than sitting beside it, so no host calls two roots.
/// </summary>
public static class AspNetServiceDefaultsExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		///     The full ASP.NET root, and the one call an application host makes: the shared observability
		///     root, ASP.NET Core metrics, request tracing filtered to observed endpoints, and the health
		///     rail with its <c>self</c> liveness check.
		/// </summary>
		/// <returns>The same <paramref name="builder" /> for chaining.</returns>
		public IHostApplicationBuilder AddAspNetServiceDefaults() =>
			builder
				.AddServiceDefaults(
					configureTracing: static tracing =>
						tracing.AddAspNetCoreInstrumentation(static options =>
							options.Filter = AspNetTraceFilter.Include),
					configureMetrics: static metrics => metrics.AddAspNetCoreInstrumentation())
				.AddDefaultHealthChecks();

		/// <summary>
		///     The root for a host that serves static content only — identical to
		///     <see cref="AddAspNetServiceDefaults" /> minus request tracing. An asset host has no database,
		///     no transport, and no downstream, so its spans would be asset fetches with nothing to
		///     correlate against; its traffic and usage signal comes from metrics instead.
		/// </summary>
		/// <returns>The same <paramref name="builder" /> for chaining.</returns>
		public IHostApplicationBuilder AddAssetHostServiceDefaults() =>
			builder
				.AddServiceDefaults(configureMetrics: static metrics => metrics.AddAspNetCoreInstrumentation())
				.AddDefaultHealthChecks();
	}
}
