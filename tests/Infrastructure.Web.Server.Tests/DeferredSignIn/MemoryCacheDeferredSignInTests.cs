using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Norse.Infrastructure.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.Tests.DeferredSignIn;

public sealed class MemoryCacheDeferredSignInTests
{
	readonly MemoryCacheDeferredSignIn _sut = new(new MemoryCache(new MemoryCacheOptions()));

	[Fact]
	void StashSignIn_then_TryConsume_returns_the_stashed_sign_in()
	{
		var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "buvy")]));
		var properties = new AuthenticationProperties { IsPersistent = true };

		var key = _sut.StashSignIn("Identity.Application", principal, properties);
		var found = _sut.TryConsume(key, out var action);

		found.ShouldBeTrue();
		action.Scheme.ShouldBe("Identity.Application");
		action.SignOut.ShouldBeFalse();
		action.Principal.ShouldBeSameAs(principal);
		action.Properties.ShouldBeSameAs(properties);
	}

	[Fact]
	void StashSignOut_then_TryConsume_returns_the_stashed_sign_out()
	{
		var key = _sut.StashSignOut("Identity.Application");
		var found = _sut.TryConsume(key, out var action);

		found.ShouldBeTrue();
		action.Scheme.ShouldBe("Identity.Application");
		action.SignOut.ShouldBeTrue();
		action.Principal.ShouldBeNull();
	}

	[Fact]
	void TryConsume_with_an_unknown_key_returns_false()
	{
		var found = _sut.TryConsume(Guid.NewGuid().ToString(), out var action);

		found.ShouldBeFalse();
		action.ShouldBeNull();
	}

	[Fact]
	void TryConsume_is_one_time_use()
	{
		var key = _sut.StashSignOut("Identity.Application");

		_sut.TryConsume(key, out _).ShouldBeTrue();
		_sut.TryConsume(key, out var secondAction).ShouldBeFalse();

		secondAction.ShouldBeNull();
	}
}
