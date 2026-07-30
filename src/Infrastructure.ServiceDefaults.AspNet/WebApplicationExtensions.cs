using Microsoft.AspNetCore.Builder;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// Probe endpoint mapping for ASP.NET hosts. Keeps the Aspire-conventional method name — it is the
/// paths that follow the Kubernetes standard, not the method that maps them.
/// </summary>
public static class WebApplicationExtensions
{
	/// <param name="app">The web application.</param>
	extension(WebApplication app)
	{
		/// <summary>
		/// Maps <see cref="HealthEndpoints.Liveness"/> (only <c>live</c>-tagged checks) and
		/// <see cref="HealthEndpoints.Readiness"/> (every registered check). Both are mapped in every
		/// environment, because an orchestrator's probes are required in production or the container
		/// never passes its gates; both are anonymous, because a probe arrives with no credentials;
		/// both are excluded from HTTP metrics here, and from tracing by the default trace filter,
		/// which knows these two paths. The default plain-text response writer is deliberate — no
		/// check name, dependency topology, or timing is disclosed.
		/// </summary>
		/// <returns>The same <paramref name="app"/> for chaining.</returns>
		public WebApplication MapDefaultEndpoints()
		{
			app.MapHealthChecks(HealthEndpoints.Liveness, new()
			{
				Predicate = static registration => registration.Tags.Contains("live"),
			})
				.AllowAnonymous()
				.DisableHttpMetrics();
			app.MapHealthChecks(HealthEndpoints.Readiness)
				.AllowAnonymous()
				.DisableHttpMetrics();
			return app;
		}
	}
}
