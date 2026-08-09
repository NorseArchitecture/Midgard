using Microsoft.AspNetCore.Http;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
///     The default request-tracing predicate. Admits application traffic and rejects probe, framework,
///     and static-asset paths — traffic that is volume without signal.
/// </summary>
/// <remarks>
///     <para>
///         This predicate matches on the request path, and that is forced rather than chosen. OpenTelemetry
///         invokes <c>AspNetCoreTraceInstrumentationOptions.Filter</c> while handling the
///         <c>Microsoft.AspNetCore.Hosting.HttpRequestIn.Start</c> event, which fires before the routing
///         middleware. <see cref="EndpointHttpContextExtensions.GetEndpoint" /> returns <see langword="null" />
///         at that point, so no endpoint-metadata convention can reach this decision — the request path is
///         what the pipeline has produced by then.
///     </para>
///     <para>
///         Metrics are the mirror image: they are recorded at request end, after routing, so the framework's
///         own <c>DisableHttpMetrics()</c> endpoint convention works there and is what hosts call. The two
///         signals are suppressed by two different mechanisms because they are decided at two different
///         points in the pipeline.
///     </para>
/// </remarks>
static class AspNetTraceFilter
{
	/// <summary>
	///     The gRPC health service's route prefix. Its full route is <c>/grpc.health.v1.Health/Check</c>,
	///     but the prefix also covers <c>Watch</c> and any future version of the service.
	/// </summary>
	const string GrpcHealthPrefix = "/grpc.health.";

	/// <summary>
	///     The framework-content prefix, covering Blazor's <c>/_framework</c> and <c>/_content</c> trees
	///     and the <c>/_blazor</c> circuit endpoint in one test.
	/// </summary>
	const string FrameworkPrefix = "/_";

	/// <summary>Returns <see langword="true" /> when the request should be traced.</summary>
	internal static bool Include(HttpContext context) =>
		context.Request.Path.Value is not string path || !IsExcluded(path);

	static bool IsExcluded(string path) =>
		// The probe paths are matched case-insensitively because ASP.NET route matching is, so a
		// probe sent to /LIVEZ reaches the endpoint and must be filtered the same way.
		path.StartsWith(HealthEndpoints.Liveness, StringComparison.OrdinalIgnoreCase) ||
		path.StartsWith(HealthEndpoints.Readiness, StringComparison.OrdinalIgnoreCase) ||
		// A gRPC route is a protobuf full name and is case-sensitive; so is the framework prefix.
		path.StartsWith(GrpcHealthPrefix, StringComparison.Ordinal) ||
		path.StartsWith(FrameworkPrefix, StringComparison.Ordinal) ||
		Path.HasExtension(path);
}
