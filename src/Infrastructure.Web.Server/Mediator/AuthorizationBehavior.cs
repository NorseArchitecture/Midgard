using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// Evaluates the policy the generator baked in from the service method's <c>[Authorize(Policy=...)]</c>
/// attribute (spec §2.5) against the principal <paramref name="principalAccessor"/> supplies. The
/// principal source is deliberately the host adapter's problem, not this behavior's — no
/// <c>IHttpContextAccessor</c> here: it's explicitly unsupported inside a live Blazor Server circuit
/// (valid only for the initial synchronous render, null/stale after SignalR reconnection), exactly the
/// path this feature exists to make safe. Not authenticated at all → <see cref="ErrorCategory.Unauthorized"/>;
/// authenticated but the policy fails → <see cref="ErrorCategory.Forbidden"/>.
/// </summary>
sealed class AuthorizationBehavior<TRequest, TResponse>(
	string policyName, IAuthorizationService authorizationService, Func<ValueTask<ClaimsPrincipal>> principalAccessor)
	: IBehavior<TRequest, TResponse>
	where TResponse : notnull
{
	public async ValueTask<Outcome<TResponse>> Handle(TRequest request, CancellationToken cancellationToken, BehaviorDelegate<TResponse> next)
	{
		var user = await principalAccessor().ConfigureAwait(false);
		var result = await authorizationService.AuthorizeAsync(user, policyName).ConfigureAwait(false);

		if (!result.Succeeded)
		{
			return Outcome<TResponse>.Err(user.Identity is { IsAuthenticated: true } ? ErrorCategory.Forbidden : ErrorCategory.Unauthorized);
		}

		return await next().ConfigureAwait(false);
	}
}
