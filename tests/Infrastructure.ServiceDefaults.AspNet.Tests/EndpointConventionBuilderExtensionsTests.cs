using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class EndpointConventionBuilderExtensionsTests
{
	[Fact]
	void Disabling_norse_observability_suppresses_http_metrics_and_stamps_the_tracing_marker()
	{
		var app = WebApplication.CreateSlimBuilder().Build();
		app.MapGet("/assets", static () => Results.Ok()).DisableNorseObservability();
		var metadata = ((IEndpointRouteBuilder)app).DataSources
			.SelectMany(static source => source.Endpoints)
			.ShouldHaveSingleItem()
			.Metadata;
		metadata.GetMetadata<IDisableHttpMetricsMetadata>().ShouldNotBeNull();
		metadata.GetMetadata<DisableNorseObservabilityMetadata>().ShouldNotBeNull();
	}

	[Fact]
	void An_ordinary_endpoint_carries_neither_marker()
	{
		var app = WebApplication.CreateSlimBuilder().Build();
		app.MapGet("/ping", static () => Results.Ok());
		var metadata = ((IEndpointRouteBuilder)app).DataSources
			.SelectMany(static source => source.Endpoints)
			.ShouldHaveSingleItem()
			.Metadata;
		metadata.GetMetadata<IDisableHttpMetricsMetadata>().ShouldBeNull();
		metadata.GetMetadata<DisableNorseObservabilityMetadata>().ShouldBeNull();
	}

	[Fact]
	void The_trace_filter_rejects_an_unobserved_endpoint_and_admits_an_ordinary_one()
	{
		DefaultHttpContext
			unobserved = new(),
			ordinary = new();
		unobserved.SetEndpoint(new(null, new(new DisableNorseObservabilityMetadata()), "unobserved"));
		ordinary.SetEndpoint(new(null, EndpointMetadataCollection.Empty, "ordinary"));
		AspNetTraceFilter.Include(unobserved).ShouldBeFalse();
		AspNetTraceFilter.Include(ordinary).ShouldBeTrue();
	}

	[Fact]
	void The_aspnet_layer_references_aspnetcore_and_the_base_layer_still_does_not()
	{
		typeof(AspNetTraceFilter).Assembly
			.GetReferencedAssemblies()
			.ShouldContain(a => a.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
		typeof(HostApplicationBuilderExtensions).Assembly
			.GetReferencedAssemblies()
			.ShouldAllBe(a => !a.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
	}
}
