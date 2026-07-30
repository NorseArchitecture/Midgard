using Microsoft.AspNetCore.Builder;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// Endpoint conventions for excluding high-volume, low-signal traffic from observability.
/// </summary>
public static class EndpointConventionBuilderExtensions
{
	/// <param name="builder">The endpoint convention builder.</param>
	extension<TBuilder>(TBuilder builder)
		where TBuilder : IEndpointConventionBuilder
	{
		/// <summary>
		/// Excludes the endpoint from both ASP.NET Core HTTP metrics and Norse request tracing — one
		/// call for traffic that is volume without signal, such as static assets and probe endpoints.
		/// Logging is unaffected; log volume is controlled by log level, not per endpoint.
		/// </summary>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public TBuilder DisableNorseObservability()
		{
			builder.DisableHttpMetrics();
			builder.Add(static endpoint => endpoint.Metadata.Add(new DisableNorseObservabilityMetadata()));
			return builder;
		}
	}
}
