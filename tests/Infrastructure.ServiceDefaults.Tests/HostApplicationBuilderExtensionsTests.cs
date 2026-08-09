using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Norse.Infrastructure.ServiceDefaults.Tests;

public sealed class HostApplicationBuilderExtensionsTests
{
	[Fact]
	void Add_default_health_checks_registers_the_self_liveness_check()
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.AddDefaultHealthChecks();
		using var host = builder.Build();
		var registration = host.Services
			.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
			.Value.Registrations.ShouldHaveSingleItem();
		registration.Name.ShouldBe("self");
		registration.Tags.ShouldContain("live");
	}

	[Fact]
	void Service_defaults_stamp_the_resource_with_service_identity()
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
		{
			ApplicationName = "Norse.TestHost",
			EnvironmentName = "Testing"
		});
		builder.AddServiceDefaults();
		using var host = builder.Build();
		var attributes = host.Services
			.GetRequiredService<TracerProvider>()
			.GetResource().Attributes
			.ToDictionary(a => a.Key, a => a.Value);
		attributes["service.name"].ShouldBe("Norse.TestHost");
		attributes.ShouldContainKey("service.instance.id");
		attributes["deployment.environment.name"].ShouldBe("Testing");
	}

	[Fact]
	void Service_defaults_keep_the_console_provider_and_enrich_the_otel_logger()
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.AddServiceDefaults();
		using var host = builder.Build();
		host.Services.GetServices<ILoggerProvider>().ShouldContain(p => p is ConsoleLoggerProvider);
		var options = host.Services.GetRequiredService<IOptionsMonitor<OpenTelemetryLoggerOptions>>().CurrentValue;
		options.IncludeFormattedMessage.ShouldBeTrue();
		options.IncludeScopes.ShouldBeTrue();
		options.ParseStateValues.ShouldBeTrue();
	}

	[Fact]
	void Norse_activity_sources_are_captured_and_foreign_sources_are_not()
	{
		List<Activity> exported = [];
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.AddServiceDefaults();
		builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
		using var host = builder.Build();
		var tracerProvider = host.Services.GetRequiredService<TracerProvider>();
		using ActivitySource
			norse = new("Norse.Test"),
			foreign = new("Foreign.Test");
		norse.StartActivity("norse-op")?.Dispose();
		foreign.StartActivity("foreign-op")?.Dispose();
		tracerProvider.ForceFlush();
		exported.ShouldHaveSingleItem().OperationName.ShouldBe("norse-op");
	}

	[Fact]
	void Norse_meters_are_captured_by_the_wildcard_subscription()
	{
		List<Metric> exported = [];
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.AddServiceDefaults();
		builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddInMemoryExporter(exported));
		using var host = builder.Build();
		var meterProvider = host.Services.GetRequiredService<MeterProvider>();
		using Meter meter = new("Norse.TestMeter");
		meter.CreateCounter<long>("norse_counter").Add(1);
		meterProvider.ForceFlush();
		exported.ShouldContain(m => m.Name == "norse_counter");
	}

	[Fact]
	void Service_defaults_register_no_health_checks()
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.AddServiceDefaults();
		using var host = builder.Build();
		host.Services.GetService<IOptions<HealthCheckServiceOptions>>()
			?.Value.Registrations.ShouldBeEmpty();
	}

	[Fact]
	async Task A_host_with_no_otlp_endpoint_builds_and_starts_cleanly()
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.AddServiceDefaults();
		using var host = builder.Build();
		await host.StartAsync(TestContext.Current.CancellationToken);
		await host.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	async Task A_host_with_an_otlp_endpoint_builds_and_starts_cleanly()
	{
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.Configuration.AddInMemoryCollection(
			[new KeyValuePair<string, string?>("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")]);
		builder.AddServiceDefaults();
		using var host = builder.Build();
		await host.StartAsync(TestContext.Current.CancellationToken);
		await host.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	void The_service_defaults_assembly_references_nothing_from_aspnetcore()
	{
		typeof(HostApplicationBuilderExtensions).Assembly
			.GetReferencedAssemblies()
			.ShouldAllBe(a => !a.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
	}

	[Fact]
	void A_forwarded_tracing_delegate_is_invoked_alongside_the_norse_wildcard()
	{
		List<Activity> exported = [];
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.AddServiceDefaults(configureTracing: static tracing => tracing.AddSource("Forwarded.Probe"));
		builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
		using var host = builder.Build();
		var tracerProvider = host.Services.GetRequiredService<TracerProvider>();
		using ActivitySource
			norse = new("Norse.Test"),
			forwarded = new("Forwarded.Probe");
		norse.StartActivity("norse-op")?.Dispose();
		forwarded.StartActivity("forwarded-op")?.Dispose();
		tracerProvider.ForceFlush();
		exported.Select(a => a.OperationName).ShouldBe(["norse-op", "forwarded-op"], ignoreOrder: true);
	}

	[Fact]
	void A_forwarded_metrics_delegate_is_invoked_alongside_the_norse_wildcard()
	{
		List<Metric> exported = [];
		var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.AddServiceDefaults(configureMetrics: static metrics => metrics.AddMeter("Forwarded.Probe"));
		builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddInMemoryExporter(exported));
		using var host = builder.Build();
		var meterProvider = host.Services.GetRequiredService<MeterProvider>();
		using Meter
			norse = new("Norse.TestMeter"),
			forwarded = new("Forwarded.Probe");
		norse.CreateCounter<long>("norse_counter").Add(1);
		forwarded.CreateCounter<long>("forwarded_counter").Add(1);
		meterProvider.ForceFlush();
		string[] names = [.. exported.Select(static metric => metric.Name)];
		names.ShouldContain("norse_counter");
		names.ShouldContain("forwarded_counter");
	}
}
