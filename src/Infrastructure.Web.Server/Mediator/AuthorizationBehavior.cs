using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Authorization;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     Evaluates the policies the request type declares via <c>[Authorize(Policy = ...)]</c> (read once
///     per closed type by <see cref="PolicyCache{TRequest}" />) against the principal
///     <see cref="IPrincipalAccessor" /> supplies. Not authenticated at all →
///     <see cref="ErrorCategory.Unauthorized" />; authenticated but a policy fails →
///     <see cref="ErrorCategory.Forbidden" />. On the wire path this runs behind ASP.NET Core's endpoint
///     [Authorize] wall — defense in depth, same policy, same decision; this behavior is the single
///     source of Unauthorized/Forbidden as data.
///     <para>
///         Freshness is evaluated last (plan ruling T4-3): every operation policy first, and any failure there
///         answers bare. Only a caller every operation policy admits is evaluated against
///         <see cref="NorsePolicies.RecentAuthentication" />, and only a <see cref="ReauthenticationRequiredReason" />
///         on an authenticated caller maps to the re-authentication hop: <see cref="ErrorCategory.Forbidden" />
///         carrying <see cref="NorseErrorCodes.ReauthenticationRequired" /> and a server-built
///         <see cref="Problem.NextUrl" /> under the live path base.
///     </para>
/// </summary>
sealed class AuthorizationBehavior<TRequest, TResponse>(
	IAuthorizationService authorizationService,
	IPrincipalAccessor principalAccessor,
	IHttpContextAccessor httpContextAccessor) :
	IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, BehaviorDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		var user = await principalAccessor.GetPrincipalAsync(cancellationToken).ConfigureAwait(false);
		foreach (var policy in PolicyCache<TRequest>.OperationPolicies)
		{
			var result = await authorizationService.AuthorizeAsync(user, policy).ConfigureAwait(false);
			if (!result.Succeeded)
				return Refused(user);
		}

		if (PolicyCache<TRequest>.RequiresRecentAuthentication)
		{
			var freshness = await authorizationService.AuthorizeAsync(user, NorsePolicies.RecentAuthentication)
				.ConfigureAwait(false);
			if (!freshness.Succeeded)
			{
				return user.Identity is { IsAuthenticated: true } &&
					freshness.Failure?.FailureReasons.Any(reason => reason is ReauthenticationRequiredReason) is true ?
						new(new Failed(new Problem
						{
							Category = ErrorCategory.Forbidden,
							Code = NorseErrorCodes.ReauthenticationRequired,
							NextUrl = ReauthenticationHop.NextUrl(
								httpContextAccessor.HttpContext?.Request.PathBase ?? PathString.Empty,
								PolicyCache<TRequest>.ReauthenticationLanding!)
						})) :
						Refused(user);
			}
		}

		return await next().ConfigureAwait(false);
	}

	static Outcome<TResponse> Refused(ClaimsPrincipal user) =>
		Outcome<TResponse>.Err(user.Identity is { IsAuthenticated: true } ?
			ErrorCategory.Forbidden :
			ErrorCategory.Unauthorized);
}
