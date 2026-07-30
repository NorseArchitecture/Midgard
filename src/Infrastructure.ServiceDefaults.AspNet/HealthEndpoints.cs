namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// The probe endpoint paths, following the Kubernetes convention rather than the Aspire template's
/// <c>/health</c> and <c>/alive</c>. Kubernetes settled on <c>/livez</c> and <c>/readyz</c>, and
/// deprecated <c>/healthz</c> in v1.16 in favor of the two specific endpoints.
/// </summary>
public static class HealthEndpoints
{
	/// <summary>
	/// The liveness probe path — restart-me semantics. Runs only <c>live</c>-tagged checks, which is
	/// the trivial <c>self</c> check alone, so it performs no I/O and is safe to poll aggressively.
	/// </summary>
	public const string Liveness = "/livez";

	/// <summary>
	/// The readiness probe path — send-me-traffic semantics. Runs every registered check, including
	/// any database check a provider component registered on the host's behalf.
	/// </summary>
	public const string Readiness = "/readyz";
}
