using System.Diagnostics;
using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;
using OpenTelemetry.Trace;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class WebApplicationExtensionsTests
{
	// The Probe policy's handlers come from AddNorseAuthentication(), the registration a real host makes. Its
	// bearer lanes require a Norse.Bearer scheme, the bearer-validating realm's to register: a stub stands in.
	static void AddProbeAuthorization(IHostApplicationBuilder builder)
	{
		builder.Services.AddNorseAuthentication()
			.AddScheme<AuthenticationSchemeOptions, BearerStubHandler>(NorseSchemes.Bearer, null);
		builder.Services.AddAuthorizationBuilder().AddPolicy(NorsePolicies.Probe, NorsePlatformPolicies.Probe);
	}

	static WebApplication BuildProbeHost(Action<IHostApplicationBuilder>? configure = null)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.AddAspNetServiceDefaults();
		AddProbeAuthorization(builder);
		configure?.Invoke(builder);
		var app = builder.Build();
		app.UseAuthorization();
		app.MapDefaultEndpoints();
		return app;
	}

	[Fact]
	async Task Liveness_and_readiness_both_report_healthy_when_only_the_self_check_is_registered()
	{
		await using var app = BuildProbeHost();
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		(await client.GetAsync(new Uri(HealthEndpoints.Liveness, UriKind.Relative),
				TestContext.Current.CancellationToken))
			.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await client.GetAsync(new Uri(HealthEndpoints.Readiness, UriKind.Relative),
				TestContext.Current.CancellationToken))
			.StatusCode.ShouldBe(HttpStatusCode.OK);
	}

	[Fact]
	async Task An_untagged_failing_check_fails_readiness_and_leaves_liveness_healthy()
	{
		await using var app = BuildProbeHost(static builder => builder.Services
			.AddHealthChecks()
			.AddCheck("database", static () => HealthCheckResult.Unhealthy()));
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		(await client.GetAsync(new Uri(HealthEndpoints.Liveness, UriKind.Relative),
				TestContext.Current.CancellationToken))
			.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await client.GetAsync(new Uri(HealthEndpoints.Readiness, UriKind.Relative),
				TestContext.Current.CancellationToken))
			.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
	}

	[Fact]
	async Task The_probe_response_discloses_no_check_names_or_timings()
	{
		await using var app = BuildProbeHost(static builder => builder.Services
			.AddHealthChecks()
			.AddCheck("database", static () => HealthCheckResult.Healthy()));
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		var body = await (await client.GetAsync(new Uri(HealthEndpoints.Readiness, UriKind.Relative),
				TestContext.Current.CancellationToken))
			.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		body.ShouldBe("Healthy");
		body.ShouldNotContain("database");
	}

	[Fact]
	void Both_probe_endpoints_declare_the_probe_policy_and_carry_no_http_metrics()
	{
		using var app = BuildProbeHost();
		Endpoint[] probes = [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints)];
		probes.Length.ShouldBe(2);
		probes.ShouldAllBe(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == NorsePolicies.Probe));
		probes.ShouldAllBe(e => e.Metadata.GetMetadata<IDisableHttpMetricsMetadata>() != null);
	}

	// An asset host has no identity stack, so nothing else registers the Probe policy for it: the asset root
	// must, or both probes answer 500 and the orchestrator never passes the container's gates. No explicit
	// UseAuthorization(): the WebApplication adds it once the asset root registers authorization, which is
	// exactly what the Stories host relies on.
	[Theory]
	[InlineData(HealthEndpoints.Liveness)]
	[InlineData(HealthEndpoints.Readiness)]
	async Task An_asset_host_answers_its_probes_with_no_authentication_registered(string path)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.AddAssetHostServiceDefaults();
		await using var app = builder.Build();
		app.MapDefaultEndpoints();
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
	}

	[Theory]
	[InlineData(HealthEndpoints.Liveness)]
	[InlineData(HealthEndpoints.Readiness)]
	async Task Probe_traffic_produces_no_spans(string path)
	{
		const string Sentinel = "/sentinel";
		List<Activity> exported = [];
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.AddAspNetServiceDefaults();
		AddProbeAuthorization(builder);
		builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
		await using var app = builder.Build();
		app.UseAuthorization();
		app.MapDefaultEndpoints();
		app.MapGet(Sentinel, static () => Results.Ok());
		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		_ = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
		_ = await client.GetAsync(new Uri(Sentinel, UriKind.Relative), TestContext.Current.CancellationToken);
		var tracer = app.Services.GetRequiredService<TracerProvider>();
		for (var attempt = 0; attempt < 100 && exported.Count == 0; attempt++)
		{
			tracer.ForceFlush();
			if (exported.Count == 0)
				await Task.Delay(10, TestContext.Current.CancellationToken);
		}

		exported.ShouldHaveSingleItem().DisplayName.ShouldContain(Sentinel);
	}
}

/// <summary>A stand-in <see cref="NorseSchemes.Bearer" />: authenticates nothing. The probe endpoints never reach it.</summary>
sealed class BearerStubHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
