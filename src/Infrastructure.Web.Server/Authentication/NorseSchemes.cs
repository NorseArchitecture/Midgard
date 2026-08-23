namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The platform's authentication scheme names. Public because Yggdrasil's composition root and
///     Himinbjorg#49's bearer wireup both name them.
/// </summary>
public static class NorseSchemes
{
	/// <summary>The lane selector — the only scheme any policy names by default.</summary>
	public const string Default = "Norse";

	/// <summary>The browser lane's composite: identity cookie, then anonymous, fallback owned internally.</summary>
	public const string Browser = "Norse.Browser";

	/// <summary>The anonymous handler. Never selected directly by a policy; the composite invokes it.</summary>
	public const string Anonymous = "Norse.Anonymous";

	/// <summary>
	///     The gRPC lane's composite: identity cookie, then a previously-established anonymous cookie —
	///     never minting one itself. See <see cref="NorseGrpcHandler" />.
	/// </summary>
	public const string IdentityCookieOnly = "Norse.IdentityCookieOnly";

	/// <summary>
	///     The orchestrator-probe lane. Authenticates nothing and mints nothing — a kubelet is not a
	///     browser. Its own lane rather than a fallthrough into <see cref="Browser" />, because assigning
	///     <c>NorsePolicies.Probe</c> governs authorization and does not stop authentication from running:
	///     without this lane a liveness probe would enter the browser composite and be handed a cookie.
	/// </summary>
	public const string Probe = "Norse.Probe";

	/// <summary>
	///     The machine lane. Forwards to OpenIddict's own validation scheme
	///     (<c>OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme</c>) — registered as a
	///     <c>AddPolicyScheme</c> forward, not a hand-rolled handler, since OpenIddict's validation
	///     builder always registers under its own fixed scheme name and cannot be told to use this one
	///     directly (Himinbjorg#49).
	/// </summary>
	public const string Machine = "Norse.Machine";
}
