using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

sealed class MemoryCacheDeferredSignIn(IMemoryCache cache) : IDeferredSignIn
{
	static readonly MemoryCacheEntryOptions _entryOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) };

	public string StashSignIn(string scheme, ClaimsPrincipal principal, AuthenticationProperties properties)
	{
		var key = Guid.NewGuid().ToString();
		cache.Set(key, new DeferredSignInAction(scheme, SignOut: false, principal, properties), _entryOptions);
		return key;
	}

	public string StashSignOut(string scheme)
	{
		var key = Guid.NewGuid().ToString();
		cache.Set(key, new DeferredSignInAction(scheme, SignOut: true, null, null), _entryOptions);
		return key;
	}

	public bool TryConsume(string key, out DeferredSignInAction action)
	{
		if (!cache.TryGetValue(key, out DeferredSignInAction? found) || found is null)
		{
			action = null!;
			return false;
		}
		cache.Remove(key);
		action = found;
		return true;
	}

	public string BuildCompletionUrl(string key, string returnUrl) =>
		$"{DeferredSignInEndpointRouteBuilderExtensions.DefaultPattern}?key={Uri.EscapeDataString(key)}&returnUrl={Uri.EscapeDataString(returnUrl)}";
}
