using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mime;
using Microsoft.AspNetCore.Authentication;
using Norse.Abstractions.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.DeferredSignIn;

/// <summary>Completion-endpoint wiring for a deferred sign-in/out.</summary>
public static class DeferredSignInEndpointRouteBuilderExtensions
{
	/// <summary>
	///     The default route pattern <see cref="MapDeferredSignIn" /> maps — callers building a completion URL reference
	///     this rather than duplicating the literal.
	/// </summary>
	public const string DefaultPattern = "/_auth/complete";

	extension(IEndpointRouteBuilder endpoints)
	{
		/// <summary>
		///     Maps the completion endpoint for a deferred sign-in/out — a plain minimal-API endpoint (a real,
		///     distinct HTTP request, not a Blazor component), safe to write cookies from. Responds with a
		///     meta-refresh page rather than a redirect: mobile Chrome has a long-standing bug where it silently
		///     drops Set-Cookie on a 302, which would otherwise loop forever. The refresh lands on the return URL only
		///     when it is a single-slash-rooted local path; anything else lands on the PathBase root.
		/// </summary>
		[SuppressMessage("Trimming", "IL2026",
			Justification =
				"MapGet's delegate overload reflects over the supplied delegate's parameters; the delegate here is a fixed, statically-known shape.")]
		[SuppressMessage("AOT", "IL3050",
			Justification =
				"MapGet's delegate overload reflects over the supplied delegate's parameters; the delegate here is a fixed, statically-known shape.")]
		public IEndpointRouteBuilder MapDeferredSignIn(string pattern = DefaultPattern)
		{
			endpoints.MapGet(pattern,
				async (HttpContext context, IDeferredSignIn deferredSignIn, string key, string returnUrl) =>
				{
					if (!deferredSignIn.TryConsume(key, out var action))
						return Results.Unauthorized();

					if (action.SignOut)
						await context.SignOutAsync(action.Scheme).ConfigureAwait(false);
					else
						await context.SignInAsync(action.Scheme, action.Principal!, action.Properties)
							.ConfigureAwait(false);

					var landing = IsLocal(returnUrl) ?
						returnUrl :
						$"{context.Request.PathBase}/";
					return Results.Content(
						$"""<!DOCTYPE html><html><head><meta http-equiv="refresh" content="0; URL={WebUtility.HtmlEncode(landing)}" /></head><body></body></html>""",
						MediaTypeNames.Text.Html);
				}).ExcludeFromDescription();
			return endpoints;
		}
	}

	// A meta refresh is not a redirect the framework validates: the landing must be a single-slash-rooted local path,
	// or the endpoint is an open redirect. A browser strips tab and newline from the refresh URL, so a control
	// character is refused outright, and the test runs again after one percent-decode (/\t/evil collapses to //evil).
	static bool IsLocal(string url) =>
		IsSingleSlashRooted(url) && IsSingleSlashRooted(Uri.UnescapeDataString(url));

	static bool IsSingleSlashRooted(string url) =>
		url is ['/', ..] and not ['/', '/' or '\\', ..] && !url.Any(char.IsControl);
}
