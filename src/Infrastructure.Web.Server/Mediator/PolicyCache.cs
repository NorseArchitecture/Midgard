using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     Reads <c>[Authorize(Policy = ...)]</c> off <typeparamref name="TRequest" /> exactly once per
///     closed type — zero per-call reflection (spec §2.5). The runtime backstop behind the registration
///     generator's NORSE011 compile-time check: a request with no policy is a hard failure at first
///     dispatch, never an open door. The attributes are partitioned by name, never by order (plan ruling T4-3):
///     <see cref="NorsePolicies.RecentAuthentication" /> is the freshness gate, every other policy is an operation
///     policy, and a freshness-gated request names at least one operation policy and declares its
///     <see cref="ReauthenticationLandingAttribute" /> (T4-4), or the first touch throws.
/// </summary>
static class PolicyCache<TRequest>
{
	static readonly AuthorizeAttribute[] _attributes = Read();

	/// <summary>Every operation policy <typeparamref name="TRequest" /> declares, distinct, in ordinal order.</summary>
	public static IReadOnlyList<string> OperationPolicies { get; } = _attributes
		.Select(authorize => authorize.Policy!)
		.Where(policy => policy != NorsePolicies.RecentAuthentication)
		.Distinct(StringComparer.Ordinal)
		.Order(StringComparer.Ordinal)
		.ToArray() is { Length: > 0 } policies ?
		policies :
		throw new InvalidOperationException(
			$"{typeof(TRequest).Name} names {NorsePolicies.RecentAuthentication} but no operation policy — freshness stacks on an operation's own policy and never authorizes alone.");

	/// <summary>Whether <typeparamref name="TRequest" /> is freshness-gated.</summary>
	public static bool RequiresRecentAuthentication { get; } =
		_attributes.Any(authorize => authorize.Policy == NorsePolicies.RecentAuthentication);

	/// <summary>
	///     The path-base-relative page the request lands back on after a re-authentication; required when
	///     <see cref="RequiresRecentAuthentication" />, <see langword="null" /> when none is declared.
	/// </summary>
	public static string? ReauthenticationLanding { get; } =
		typeof(TRequest).GetCustomAttribute<ReauthenticationLandingAttribute>()?.Path is { } landing ?
			landing :
			RequiresRecentAuthentication ?
				throw new InvalidOperationException(
					$"{typeof(TRequest).Name} names {NorsePolicies.RecentAuthentication} but carries no [ReauthenticationLanding] — the server cannot author the re-authentication hop without it.") :
				null;

	static AuthorizeAttribute[] Read()
	{
		var attributes = typeof(TRequest).GetCustomAttributes<AuthorizeAttribute>().ToArray();
		return attributes.Length > 0 && attributes.All(authorize => authorize.Policy is { Length: > 0 }) ?
			attributes :
			throw new InvalidOperationException(
				$"{typeof(TRequest).Name} carries no [Authorize(Policy = ...)] — every request names its policy, AuthNPolicies.Public included.");
	}
}
