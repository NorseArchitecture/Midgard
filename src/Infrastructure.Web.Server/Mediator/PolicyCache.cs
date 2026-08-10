using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     Reads <c>[Authorize(Policy = ...)]</c> off <typeparamref name="TRequest" /> exactly once per
///     closed type — zero per-call reflection (spec §2.5). The runtime backstop behind the registration
///     generator's NORSE011 compile-time check: a request with no policy is a hard failure at first
///     dispatch, never an open door.
/// </summary>
static class PolicyCache<TRequest>
{
	/// <summary>The policy name <typeparamref name="TRequest" /> declares.</summary>
	public static string Policy { get; } =
		typeof(TRequest).GetCustomAttribute<AuthorizeAttribute>() is { Policy.Length: > 0 } authorize ?
			authorize.Policy :
			throw new InvalidOperationException(
				$"{typeof(TRequest).Name} carries no [Authorize(Policy = ...)] — every request names its policy, AuthNPolicies.Public included.");
}
