using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

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
		/// Composes the shared observability root: resource attributes (<c>service.name</c> from
		/// <c>OTEL_SERVICE_NAME</c> or the application name, <c>service.version</c>,
		/// <c>service.instance.id</c>, <c>deployment.environment.name</c>), always-on console logging
		/// alongside the OpenTelemetry <see cref="Microsoft.Extensions.Logging.ILogger"/> provider,
		/// tracing and metrics with the <c>Norse.*</c> wildcard subscription plus .NET runtime
		/// instrumentation. Every container calls this — there is no opt-out and no lightweight
		/// variant. Health registration is deliberately not composed here: registration is
		/// participation, and participation arrives with the layer that guarantees a consumer
		/// (see <see cref="AddDefaultHealthChecks"/>).
		/// </summary>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddServiceDefaults()
		{
			builder.Logging
				.AddConsole()
				.AddOpenTelemetry(static options =>
				{
					options.IncludeFormattedMessage = true;
					options.IncludeScopes = true;
					options.ParseStateValues = true;
				});
			builder.Services
				.AddOpenTelemetry()
				.ConfigureResource(resource => resource
					.AddService(
						builder.Configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName,
						serviceVersion: Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
					.AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
				.WithTracing(static tracing => tracing.AddSource("Norse.*"))
				.WithMetrics(static metrics => metrics
					.AddMeter("Norse.*")
					.AddRuntimeInstrumentation());
			return builder;
		}

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
