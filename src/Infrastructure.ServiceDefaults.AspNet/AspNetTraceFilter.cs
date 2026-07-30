using Microsoft.AspNetCore.Http;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// The default request-tracing predicate. Admits application traffic and rejects any endpoint that
/// declared itself unobserved via <c>DisableNorseObservability()</c> — no path matching, so the
/// filter never has to be kept in sync with a route table.
/// </summary>
static class AspNetTraceFilter
{
	/// <summary>Returns <see langword="true"/> when the request should be traced.</summary>
	internal static bool Include(HttpContext context) =>
		context.GetEndpoint()?.Metadata.GetMetadata<DisableNorseObservabilityMetadata>() is null;
}
