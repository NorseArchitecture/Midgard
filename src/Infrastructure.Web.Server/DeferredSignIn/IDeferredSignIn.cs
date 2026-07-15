using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

/// <summary>
/// Stashes a sign-in or sign-out that cannot complete on the current request (an already-established
/// Blazor Server interactive circuit, where <c>HttpContext.Response.HasStarted</c> is already true) so
/// it can be completed on a genuine, later HTTP request instead. Zero domain knowledge — reusable by
/// any future realm hosting cookie-based auth behind an interactive Blazor Server component.
/// </summary>
public interface IDeferredSignIn
{
	/// <summary>Stashes a pending sign-in. Returns a one-time completion key.</summary>
	string StashSignIn(string scheme, ClaimsPrincipal principal, AuthenticationProperties properties);

	/// <summary>Stashes a pending sign-out. Returns a one-time completion key.</summary>
	string StashSignOut(string scheme);

	/// <summary>Consumes (and removes) a completion key. Returns false if the key is unknown or expired.</summary>
	bool TryConsume(string key, out DeferredSignInAction action);
}

/// <summary>What to do to complete a deferred sign-in/out. <see cref="Principal"/> is null for sign-out.</summary>
public sealed record DeferredSignInAction(string Scheme, bool SignOut, ClaimsPrincipal? Principal, AuthenticationProperties? Properties);
