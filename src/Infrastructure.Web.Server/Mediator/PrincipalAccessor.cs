using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
/// The scoped principal context (spec §2.4, the Bogard scoped-context pattern). An explicit
/// <see cref="Seed"/> from a channel adapter (the gRPC seeding interceptor) always wins and is
/// deterministic for request-scoped channels. In a circuit scope — never seeded, because a circuit
/// outlives login/logout — the accessor defers to <see cref="AuthenticationStateProvider"/> live on
/// every access. A scope neither seeded nor circuit-shaped fails loudly: no silent anonymous.
/// </summary>
sealed class PrincipalAccessor(IServiceProvider services) : IPrincipalAccessor
{
	ClaimsPrincipal? _seeded;

	internal void Seed(ClaimsPrincipal principal) =>
		_seeded = principal;

	public async ValueTask<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default) =>
		_seeded ??
			(services.GetService<AuthenticationStateProvider>() is { } provider ?
				(await provider.GetAuthenticationStateAsync().ConfigureAwait(false)).User :
				throw new InvalidOperationException(
					"No principal is available in this scope. A gRPC channel must register Midgard's PrincipalSeedingInterceptor; a circuit scope must have an AuthenticationStateProvider. Neither was found."));
}
