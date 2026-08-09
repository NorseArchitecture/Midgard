using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Norse.Infrastructure.ServiceDefaults;

/// <summary>
///     Extension methods for <see cref="IHostApplicationBuilder" /> composing the shared observability
///     root — the one surface every container calls regardless of host shape.
/// </summary>
public static class HostApplicationBuilderExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		///     Composes the shared observability root: resource attributes (<c>service.name</c> from
		///     <c>OTEL_SERVICE_NAME</c> or the application name, <c>service.version</c>,
		///     <c>service.instance.id</c>, <c>deployment.environment.name</c>), always-on console logging
		///     alongside the OpenTelemetry <see cref="Microsoft.Extensions.Logging.ILogger" /> provider,
		///     tracing and metrics with the <c>Norse.*</c> wildcard subscription plus .NET runtime
		///     instrumentation. Every container calls this — there is no opt-out and no lightweight
		///     variant. Health registration is deliberately not composed here: registration is
		///     participation, and participation arrives with the layer that guarantees a consumer
		///     (see <see cref="AddDefaultHealthChecks" />).
		/// </summary>
		/// <param name="configureTracing">
		///     Optional additional tracing configuration, invoked inside this method's own
		///     <c>WithTracing</c> block after the <c>Norse.*</c> subscription. Additive only — nothing
		///     passed here can subtract emission. Used by a higher layer (for example the ASP.NET root) to
		///     contribute to this single OpenTelemetry composition instead of opening a second one.
		/// </param>
		/// <param name="configureMetrics">
		///     Optional additional metrics configuration, invoked inside this method's own
		///     <c>WithMetrics</c> block after the <c>Norse.*</c> subscription and runtime instrumentation.
		///     Additive only, on the same terms as <paramref name="configureTracing" />.
		/// </param>
		/// <returns>The same <paramref name="builder" /> for chaining.</returns>
		public IHostApplicationBuilder AddServiceDefaults(
			Action<TracerProviderBuilder>? configureTracing = null,
			Action<MeterProviderBuilder>? configureMetrics = null)
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
						serviceVersion: Assembly.GetEntryAssembly()
							?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
					.AddAttributes([
						new KeyValuePair<string, object>("deployment.environment.name",
							builder.Environment.EnvironmentName)
					]))
				.WithTracing(tracing =>
				{
					tracing.AddSource("Norse.*");
					configureTracing?.Invoke(tracing);
				})
				.WithMetrics(metrics =>
				{
					metrics
						.AddMeter("Norse.*")
						.AddRuntimeInstrumentation();
					configureMetrics?.Invoke(metrics);
				});
			// The guard is ours, not the SDK's: UseOtlpExporter() with no endpoint configured defaults
			// to localhost:4317 and fails on every export attempt (spec §3.7). Behind this check,
			// absence is a genuine no-op and console still works.
			if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
			{
				builder.Services.AddOpenTelemetry().UseOtlpExporter();
			}

			return builder;
		}

		/// <summary>
		///     Registers health-check services with the <c>self</c> liveness check (tagged <c>live</c>) —
		///     the host-neutral registration rail later layers hang checks on. No reporter is registered
		///     here: web hosts map endpoints in the ASP.NET layer, the worker's publisher arrives with the
		///     messaging layer, and the migrations service never participates (its exit code is the contract).
		/// </summary>
		/// <returns>The same <paramref name="builder" /> for chaining.</returns>
		public IHostApplicationBuilder AddDefaultHealthChecks()
		{
			builder.Services
				.AddHealthChecks()
				.AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);
			return builder;
		}
	}
}
