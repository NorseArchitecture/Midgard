using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Evaluates the policy the request type declares via <c>[Authorize(Policy = ...)]</c> (read once
/// per closed type by <see cref="PolicyCache{TRequest}"/>) against the principal
/// <see cref="IPrincipalAccessor"/> supplies. Not authenticated at all →
/// <see cref="ErrorCategory.Unauthorized"/>; authenticated but the policy fails →
/// <see cref="ErrorCategory.Forbidden"/>. On the wire path this runs behind ASP.NET Core's endpoint
/// [Authorize] wall — defense in depth, same policy, same decision; this behavior is the single
/// source of Unauthorized/Forbidden as data.
/// </summary>
sealed class AuthorizationBehavior<TRequest, TResponse>(
	IAuthorizationService authorizationService, IPrincipalAccessor principalAccessor) :
	IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, BehaviorDelegate<TResponse> next, CancellationToken cancellationToken = default)
	{
		var user = await principalAccessor.GetPrincipalAsync(cancellationToken).ConfigureAwait(false);
		var result = await authorizationService.AuthorizeAsync(user, PolicyCache<TRequest>.Policy).ConfigureAwait(false);
		return !result.Succeeded ?
			Outcome<TResponse>.Err(user.Identity is { IsAuthenticated: true } ? ErrorCategory.Forbidden : ErrorCategory.Unauthorized) :
			await next().ConfigureAwait(false);
	}
}
