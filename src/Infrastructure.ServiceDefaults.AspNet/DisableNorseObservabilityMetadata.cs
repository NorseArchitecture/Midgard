namespace Norse.Infrastructure.ServiceDefaults.AspNet;

/// <summary>
/// Endpoint metadata marking an endpoint as excluded from Norse request tracing. Stamped by
/// <c>DisableNorseObservability()</c> and read by the default trace filter, so exclusion is decided
/// by endpoint metadata rather than by matching request paths.
/// </summary>
sealed class DisableNorseObservabilityMetadata;
