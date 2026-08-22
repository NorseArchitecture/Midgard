using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Norse.Abstractions.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     The scoped principal context (spec §2.4, the Bogard scoped-context pattern). An explicit
///     <see cref="Seed" /> from a channel adapter (the gRPC seeding interceptor) always wins and is
///     deterministic for request-scoped channels. In a circuit scope — never seeded, because a circuit
///     outlives login/logout — the accessor defers to <see cref="AuthenticationStateProvider" /> live on
///     every access. A scope neither seeded nor circuit-shaped fails loudly: no silent anonymous.
/// </summary>
sealed class PrincipalAccessor(IServiceProvider services) : IPrincipalAccessor
{
	ClaimsPrincipal? _seeded;

	public async ValueTask<ClaimsPrincipal> GetPrincipalAsync(CancellationToken cancellationToken = default) =>
		_seeded ??
		(services.GetService<AuthenticationStateProvider>() is { } provider ?
			(await provider.GetAuthenticationStateAsync().ConfigureAwait(false)).User :
			throw new InvalidOperationException(
				"No principal is available in this scope. A gRPC channel must register Midgard's PrincipalSeedingInterceptor; a circuit scope must have an AuthenticationStateProvider. Neither was found."));

	internal void Seed(ClaimsPrincipal principal)
	{
		ArgumentNullException.ThrowIfNull(principal);

		// The backstop, not the gate. UseAuthorization() rejects a lane that established no principal long
		// before anything reaches here, so this throw should be unreachable -- which is exactly why it
		// exists. A future lane that forgets to declare its schemes fails loudly at the seam instead of
		// quietly seeding an empty principal and letting a RequireAssertion(_ => true) policy wave it
		// through, which is the hole this whole design closes.
		var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);

		// Guid.Empty parses and is not an identity. Every mint is Guid.NewGuid(), so the all-zero value can
		// only arrive from a default-constructed claim or a bug -- exactly the class of thing a backstop
		// exists to catch, and precisely the value that would look like a valid namespace to the idempotency
		// spine (Midgard#58) while identifying nobody.
		if (!Guid.TryParse(identifier, out var id) || id == Guid.Empty)
		{
			throw new InvalidOperationException(
				"A principal reaching the mediator must carry a GUID identifier. Received "
				+ (identifier is null ? "no identifier claim" : $"'{identifier}'")
				+ ". Every lane establishes a principal before authorization runs -- see "
				+ "Glitnir/docs/Platform/specs/2026-08-21-principal-at-the-door-design.md §2.6.");
		}

		_seeded = principal;
	}
}
