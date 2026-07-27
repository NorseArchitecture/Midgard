using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class PrincipalAccessorTests
{
	static ClaimsPrincipal Authenticated() =>
		new(new ClaimsIdentity(authenticationType: "test"));

	[Fact]
	async Task Seeded_principal_wins_and_never_touches_the_authentication_state_provider()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);
		var seeded = Authenticated();
		accessor.Seed(seeded);

		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(seeded);
	}

	[Fact]
	async Task Unseeded_scope_with_an_authentication_state_provider_fetches_live()
	{
		var user = Authenticated();
		var provider = Substitute.For<AuthenticationStateProvider>();
		provider.GetAuthenticationStateAsync().Returns(new AuthenticationState(user));
		var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();

		PrincipalAccessor accessor = new(services);

		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(user);
	}

	[Fact]
	async Task Unseeded_scope_with_no_provider_fails_loudly()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);

		await Should.ThrowAsync<InvalidOperationException>(async () =>
			await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	async Task Circuit_principal_is_live_not_cached_across_mid_circuit_revalidation()
	{
		// Spec §2.4 remand (2026-07-27, security-relevant): a RevalidatingAuthenticationStateProvider
		// can log the user out mid-circuit; the accessor must reflect that on the very next access,
		// never keep authorizing the old identity for the life of the circuit scope.
		var before = Authenticated();
		var after = new ClaimsPrincipal(new ClaimsIdentity()); // revalidation failed → anonymous
		var provider = Substitute.For<AuthenticationStateProvider>();
		provider.GetAuthenticationStateAsync().Returns(new AuthenticationState(before), new AuthenticationState(after));
		var services = new ServiceCollection().AddSingleton(provider).BuildServiceProvider();

		PrincipalAccessor accessor = new(services);

		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(before);
		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(after);
	}
}
