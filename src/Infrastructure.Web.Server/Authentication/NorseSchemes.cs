namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The platform's authentication scheme names. Public because Yggdrasil's composition root and
///     Himinbjörg's bearer wireup both name them.
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
	///     The machine lane: a <see cref="NorseBearerLaneHandler" /> that authenticates <see cref="Bearer" /> and
	///     stamps the result <c>NorseLanes.Machine</c>. The lane selector routes REST facade controllers here.
	/// </summary>
	public const string Machine = "Norse.Machine";

	/// <summary>
	///     The user lane: a <see cref="NorseBearerLaneHandler" /> that authenticates <see cref="Bearer" /> and
	///     stamps the result <c>NorseLanes.User</c>.
	/// </summary>
	public const string User = "Norse.User";

	/// <summary>
	///     The one bearer source, a contract Midgard declares and never implements: the bearer-validating realm
	///     registers it (Himinbjörg, as a forward to its validation scheme). Both bearer lanes authenticate it by
	///     name and delegate challenge and forbid to it; a host that calls
	///     <see cref="AuthenticationBuilderExtensions.AddNorseAuthentication" /> without registering it fails at
	///     startup.
	/// </summary>
	public const string Bearer = "Norse.Bearer";
}
