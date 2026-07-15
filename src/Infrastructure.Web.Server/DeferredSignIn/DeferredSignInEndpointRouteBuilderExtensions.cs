using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

/// <summary>Completion-endpoint wiring for a deferred sign-in/out.</summary>
public static class DeferredSignInEndpointRouteBuilderExtensions
{
	/// <summary>The default route pattern <see cref="MapDeferredSignIn"/> maps — callers building a completion URL reference this rather than duplicating the literal.</summary>
	public const string DefaultPattern = "/_auth/complete";

	/// <summary>
	/// Maps the completion endpoint for a deferred sign-in/out — a plain minimal-API endpoint (a real,
	/// distinct HTTP request, not a Blazor component), safe to write cookies from. Responds with a
	/// meta-refresh page rather than a redirect: mobile Chrome has a long-standing bug where it silently
	/// drops Set-Cookie on a 302, which would otherwise loop forever.
	/// </summary>
	[SuppressMessage("Trimming", "IL2026", Justification = "MapGet's delegate overload reflects over the supplied delegate's parameters; the delegate here is a fixed, statically-known shape.")]
	[SuppressMessage("AOT", "IL3050", Justification = "MapGet's delegate overload reflects over the supplied delegate's parameters; the delegate here is a fixed, statically-known shape.")]
	public static IEndpointRouteBuilder MapDeferredSignIn(this IEndpointRouteBuilder endpoints, string pattern = DefaultPattern)
	{
		endpoints.MapGet(pattern, async (HttpContext context, IDeferredSignIn deferredSignIn, string key, string returnUrl) =>
		{
			if (!deferredSignIn.TryConsume(key, out var action))
				return Results.Unauthorized();

			if (action.SignOut)
				await context.SignOutAsync(action.Scheme).ConfigureAwait(false);
			else
				await context.SignInAsync(action.Scheme, action.Principal!, action.Properties).ConfigureAwait(false);

			return Results.Content(
				$"""<!DOCTYPE html><html><head><meta http-equiv="refresh" content="0; URL={System.Net.WebUtility.HtmlEncode(returnUrl)}" /></head><body></body></html>""",
				"text/html");
		});
		return endpoints;
	}
}
