using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class PrincipalAccessorTests
{
	static ClaimsPrincipal Authenticated() =>
		new(new ClaimsIdentity(authenticationType: "test"));

	static ClaimsPrincipal WithId(string? id) =>
		new(new ClaimsIdentity(id is null ? [] : [new Claim(ClaimTypes.NameIdentifier, id)], "test"));

	[Fact]
	async Task Seeded_principal_wins_and_never_touches_the_authentication_state_provider()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);
		var seeded = WithId(Guid.NewGuid().ToString("D"));
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

	[Fact]
	void Seeding_a_guid_bearing_principal_succeeds()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);

		Should.NotThrow(() => accessor.Seed(WithId(Guid.NewGuid().ToString("D"))));
	}

	[Fact]
	void Seeding_a_principal_with_no_identifier_throws()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);

		Should.Throw<InvalidOperationException>(() => accessor.Seed(WithId(null)))
			.Message.ShouldContain("GUID");
	}

	[Fact]
	void Seeding_a_principal_whose_identifier_is_not_a_guid_throws()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);

		Should.Throw<InvalidOperationException>(() => accessor.Seed(WithId("not-a-guid")));
	}

	[Fact]
	void Seeding_a_principal_whose_identifier_is_the_empty_guid_throws()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);

		// Guid.Empty parses. It is not an identity, and it is the value most likely to arrive from a
		// default-constructed claim -- so it is stated and rejected rather than left to the reader.
		Should.Throw<InvalidOperationException>(() => accessor.Seed(WithId(Guid.Empty.ToString("D"))));
	}
}
