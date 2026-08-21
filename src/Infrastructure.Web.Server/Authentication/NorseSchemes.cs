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

	/// <summary>The gRPC lane: identity cookie only, no fallback, no minting.</summary>
	public const string IdentityCookieOnly = "Norse.IdentityCookieOnly";

	/// <summary>
	///     The orchestrator-probe lane. Authenticates nothing and mints nothing — a kubelet is not a
	///     browser. Its own lane rather than a fallthrough into <see cref="Browser" />, because assigning
	///     <c>NorsePolicies.Probe</c> governs authorization and does not stop authentication from running:
	///     without this lane a liveness probe would enter the browser composite and be handed a cookie.
	/// </summary>
	public const string Probe = "Norse.Probe";

	/// <summary>
	///     The machine lane. Until Himinbjorg#49 lands its handler is
	///     <c>NorseMachineRejectionHandler</c>; #49 forwards this name to bearer instead. Registered from
	///     day one either way — forwarding to an unregistered scheme throws a handler-lookup exception
	///     rather than producing a clean 401.
	/// </summary>
	public const string Machine = "Norse.Machine";
}
