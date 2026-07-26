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
		ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "buvy")]));
		AuthenticationProperties properties = new() { IsPersistent = true };

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

	[Fact]
	void BuildCompletionUrl_CombinesTheDefaultPattern_WithEscapedKeyAndReturnUrl()
	{
		var url = _sut.BuildCompletionUrl("abc 123", "/dashboard?tab=1&x=y");

		url.ShouldBe("/_auth/complete?key=abc%20123&returnUrl=%2Fdashboard%3Ftab%3D1%26x%3Dy");
	}
}
