using Microsoft.AspNetCore.Http;

namespace Norse.Infrastructure.ServiceDefaults.AspNet.Tests;

public sealed class AspNetTraceFilterTests
{
	static HttpContext Request(string path)
	{
		DefaultHttpContext context = new();
		context.Request.Path = path;
		return context;
	}

	[Theory]
	[InlineData("/livez")]
	[InlineData("/readyz")]
	[InlineData("/LIVEZ")]
	[InlineData("/grpc.health.v1.Health/Check")]
	[InlineData("/grpc.health.v1.Health/Watch")]
	[InlineData("/_framework/blazor.boot.json")]
	[InlineData("/_content/Norse.DesignSystem/tokens.css")]
	[InlineData("/_blazor")]
	[InlineData("/app.css")]
	[InlineData("/dotnet.runtime.js")]
	[InlineData("/_framework/dotnet.native.wasm")]
	void Volume_without_signal_is_not_traced(string path) =>
		AspNetTraceFilter.Include(Request(path)).ShouldBeFalse();

	[Theory]
	[InlineData("/")]
	[InlineData("")]
	[InlineData("/ping")]
	[InlineData("/api/policies")]
	[InlineData("/api/v1.0/policies")]
	[InlineData("/Account/Login")]
	[InlineData("/norse.identity.v1.Authentication/Login")]
	void Application_traffic_is_traced(string path) =>
		AspNetTraceFilter.Include(Request(path)).ShouldBeTrue();

	[Fact]
	void The_filter_decides_without_a_routed_endpoint()
	{
		// The regression guard for the defect this design replaces. OpenTelemetry invokes Filter
		// before the routing middleware runs, so the filter sees exactly this context shape: a
		// populated path and no endpoint. A filter that consults endpoint metadata reads null here
		// and silently admits everything.
		var context = Request(HealthEndpoints.Liveness);
		context.GetEndpoint().ShouldBeNull();
		AspNetTraceFilter.Include(context).ShouldBeFalse();
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
