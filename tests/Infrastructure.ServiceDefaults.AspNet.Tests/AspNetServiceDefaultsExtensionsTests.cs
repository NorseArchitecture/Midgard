using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class AspNetServiceDefaultsExtensionsTests
{
	/// <summary>A route the filter always admits, used to prove the exporter was live.</summary>
	const string Sentinel = "/sentinel";

	static WebApplication BuildHost(
		bool applicationHost,
		List<Activity> exportedActivities,
		Action<IHostApplicationBuilder>? configure = null)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		_ = applicationHost ?
			builder.AddAspNetServiceDefaults() :
			builder.AddAssetHostServiceDefaults();
		builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));
		configure?.Invoke(builder);
		return builder.Build();
	}

	// A request's span does not exist yet when GetAsync returns. TestServer hands the response body
	// to the client in CompleteResponseAsync and only later, in DisposeContext, does
	// HostingApplicationDiagnostics stop the Activity — which is when the export processor fires.
	// ForceFlush cannot flush a span that has not ended, so a bare assert-after-GetAsync is a race
	// in BOTH directions: ShouldNotBeEmpty can miss a span that is about to land, and ShouldBeEmpty
	// can pass vacuously against a filter that does nothing. Measured at ~18/20 arriving in time.
	static async Task DrainAsync(WebApplication app, List<Activity> exported, int count)
	{
		var tracer = app.Services.GetRequiredService<TracerProvider>();
		for (var attempt = 0; attempt < 100; attempt++)
		{
			tracer.ForceFlush();
			if (exported.Count >= count)
				return;
			await Task.Delay(10, TestContext.Current.CancellationToken);
		}
		exported.Count.ShouldBeGreaterThanOrEqualTo(count, "the expected span never arrived");
	}

	// The asset host registers no tracing instrumentation at all, so there is no span to wait for
	// and no sentinel is possible — emptiness can only be established by giving the pipeline the
	// full window a span would have needed and finding nothing in it.
	static async Task SettleAsync(WebApplication app, int milliseconds = 1_000)
	{
		var tracer = app.Services.GetRequiredService<TracerProvider>();
		await Task.Delay(milliseconds, TestContext.Current.CancellationToken);
		tracer.ForceFlush();
	}

	[Fact]
	async Task An_application_host_traces_ordinary_requests()
	{
		List<Activity> exported = [];
		await using var app = BuildHost(applicationHost: true, exported);
		app.MapGet("/ping", static () => Results.Ok());
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		_ = await client.GetAsync(new Uri("/ping", UriKind.Relative), TestContext.Current.CancellationToken);
		await DrainAsync(app, exported, 1);
		exported.ShouldNotBeEmpty();
	}

	[Fact]
	async Task An_asset_host_traces_nothing()
	{
		List<Activity> exported = [];
		await using var app = BuildHost(applicationHost: false, exported);
		app.MapGet("/ping", static () => Results.Ok());
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		_ = await client.GetAsync(new Uri("/ping", UriKind.Relative), TestContext.Current.CancellationToken);
		await SettleAsync(app);
		exported.ShouldBeEmpty();
	}

	[Theory]
	[InlineData("/livez")]
	[InlineData("/grpc.health.v1.Health/Check")]
	[InlineData("/_blazor")]
	[InlineData("/app.css")]
	async Task An_application_host_does_not_trace_volume_without_signal(string path)
	{
		List<Activity> exported = [];
		await using var app = BuildHost(applicationHost: true, exported);
		app.MapGet(path, static () => Results.Ok());
		app.MapGet(Sentinel, static () => Results.Ok());
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		_ = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
		_ = await client.GetAsync(new Uri(Sentinel, UriKind.Relative), TestContext.Current.CancellationToken);
		// Waiting for the sentinel's span is what makes the emptiness claim mean something: it proves
		// the exporter was live and delivering during the window in which the excluded request's span
		// would have arrived. The excluded path contributed nothing, so the sentinel stands alone.
		await DrainAsync(app, exported, 1);
		exported.ShouldHaveSingleItem().DisplayName.ShouldContain(Sentinel);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	void Both_roots_compose_the_health_rail_with_the_self_liveness_check(bool applicationHost)
	{
		List<Activity> exported = [];
		using var app = BuildHost(applicationHost, exported);
		var registration = app.Services
			.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
			.Value.Registrations.ShouldHaveSingleItem();
		registration.Name.ShouldBe("self");
		registration.Tags.ShouldContain("live");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	void Both_roots_stamp_the_resource_the_same_way_the_base_layer_does(bool applicationHost)
	{
		List<Activity> exported = [];
		using var app = BuildHost(applicationHost, exported);
		var attributes = app.Services
			.GetRequiredService<TracerProvider>()
			.GetResource().Attributes
			.ToDictionary(a => a.Key, a => a.Value);
		attributes.ShouldContainKey("service.name");
		attributes.ShouldContainKey("service.instance.id");
		attributes.ShouldContainKey("deployment.environment.name");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	void Composing_the_base_layer_does_not_double_register_the_console_provider(bool applicationHost)
	{
		List<Activity> exported = [];
		using var app = BuildHost(applicationHost, exported);
		app.Services
			.GetServices<ILoggerProvider>()
			.Count(static provider => provider is ConsoleLoggerProvider)
			.ShouldBe(1);
	}
}
