using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Norse.Abstractions.Web.Server.Authorization;

namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
///     Probe endpoint mapping for ASP.NET hosts. Keeps the Aspire-conventional method name — it is the
///     paths that follow the Kubernetes standard, not the method that maps them.
/// </summary>
public static class WebApplicationExtensions
{
	/// <param name="app">The web application.</param>
	extension(WebApplication app)
	{
		/// <summary>
		///     Maps <see cref="HealthEndpoints.Liveness" /> (only <c>live</c>-tagged checks) and
		///     <see cref="HealthEndpoints.Readiness" /> (every registered check). Both are mapped in every
		///     environment, because an orchestrator's probes are required in production or the container
		///     never passes its gates; both carry <see cref="NorsePolicies.Probe" /> — a named policy that
		///     requires nothing, because a probe arrives with no credentials, but the exemption is now
		///     greppable and reviewable instead of an anonymity escape hatch NORSE013 would strike. Health
		///     endpoints never reach the mediator, so §2.6's principal invariant does not cover them —
		///     worth saying, because the next reader will ask. Both are excluded from HTTP
		///     metrics here, and from tracing by the default trace filter, which knows these two paths. The
		///     default plain-text response writer is deliberate — no check name, dependency topology, or
		///     timing is disclosed.
		/// </summary>
		/// <returns>The same <paramref name="app" /> for chaining.</returns>
		public WebApplication MapDefaultEndpoints()
		{
			app.MapHealthChecks(HealthEndpoints.Liveness,
					new HealthCheckOptions { Predicate = static registration => registration.Tags.Contains("live") })
				.RequireAuthorization(NorsePolicies.Probe)
				.DisableHttpMetrics();
			app.MapHealthChecks(HealthEndpoints.Readiness)
				.RequireAuthorization(NorsePolicies.Probe)
				.DisableHttpMetrics();
			return app;
		}
	}
}
