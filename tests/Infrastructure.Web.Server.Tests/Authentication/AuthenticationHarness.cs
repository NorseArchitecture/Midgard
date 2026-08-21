using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

/// <summary>
///     Drives one of the platform's <see cref="AuthenticationHandler{TOptions}" /> implementations directly
///     against a <see cref="DefaultHttpContext" />, without a full ASP.NET Core request pipeline. Built for
///     <see cref="NorseAnonymousHandler" /> (Task 5); Tasks 6 and 7 reuse it for the identity-cookie handler
///     and the browser composite.
///     <para>
///     A fresh handler instance is created on every <see cref="AuthenticateAsync" /> call, bound to whatever
///     <see cref="HttpContext" /> is current at that moment — mirroring how a real host resolves a new handler
///     per request rather than reusing one across a mint/replay cycle.
///     </para>
/// </summary>
sealed class AuthenticationHarness
{
	readonly Func<AuthenticationScheme, HttpContext, Task<IAuthenticationHandler>> _createHandler;
	readonly string _cookieName;
	readonly IDataProtectionProvider _protection;
	readonly AuthenticationScheme _scheme;
	DefaultHttpContext _context = new();

	AuthenticationHarness(AuthenticationScheme scheme, string cookieName, IDataProtectionProvider protection,
		Func<AuthenticationScheme, HttpContext, Task<IAuthenticationHandler>> createHandler)
	{
		_scheme = scheme;
		_cookieName = cookieName;
		_protection = protection;
		_createHandler = createHandler;
	}

	/// <summary>The current request — mutate its headers/cookies before calling <see cref="AuthenticateAsync" />.</summary>
	public HttpRequest Request => _context.Request;

	/// <summary>The current response's raw <c>Set-Cookie</c> header values.</summary>
	public IReadOnlyList<string> SetCookies => [.. _context.Response.Headers.SetCookie.OfType<string>()];

	/// <summary>Builds a harness wired for <see cref="NorseAnonymousHandler" /> against <see cref="NorseSchemes.Anonymous" />.</summary>
	public static AuthenticationHarness ForAnonymous()
	{
		NorseAnonymousOptions options = new();
		var monitor = Substitute.For<IOptionsMonitor<NorseAnonymousOptions>>();
		monitor.CurrentValue.Returns(options);
		monitor.Get(Arg.Any<string>()).Returns(options);

		var protection = DataProtectionProvider.Create(nameof(NorseAnonymousHandlerTests));
		AuthenticationScheme scheme = new(NorseSchemes.Anonymous, NorseSchemes.Anonymous, typeof(NorseAnonymousHandler));

		return new AuthenticationHarness(scheme, options.CookieName, protection, async (s, ctx) =>
		{
			NorseAnonymousHandler handler = new(monitor, NullLoggerFactory.Instance, UrlEncoder.Default, protection,
				TimeProvider.System);
			await handler.InitializeAsync(s, ctx);
			return handler;
		});
	}

	/// <summary>Initializes and invokes the wired handler's <see cref="IAuthenticationHandler.AuthenticateAsync" />.</summary>
	public async Task<AuthenticateResult> AuthenticateAsync()
	{
		var handler = await _createHandler(_scheme, _context);
		return await handler.AuthenticateAsync();
	}

	/// <summary>
	///     Simulates the browser's round trip: copies every cookie the response just set onto a fresh
	///     request/response pair, so the next <see cref="AuthenticateAsync" /> call sees them as incoming
	///     cookies rather than as cookies this same response already wrote.
	/// </summary>
	public void ReplayCookies()
	{
		var pairs = SetCookies.Select(CookiePair).ToArray();
		_context = new DefaultHttpContext();
		if (pairs.Length > 0)
			_context.Request.Headers.Cookie = new StringValues(pairs);
	}

	/// <summary>
	///     Sets the request's cookie header to a Data-Protection-protected payload for <paramref name="id" />,
	///     encoded exactly as <see cref="NorseAnonymousHandler" /> itself would write it.
	/// </summary>
	public AuthenticationHarness WithProtectedAnonymousPayload(Guid id)
	{
		var protector = _protection.CreateProtector(NorseAnonymousOptions.ProtectionPurpose);
		var payload = protector.Protect(id.ToString("D"));

		DefaultHttpContext scratch = new();
		scratch.Response.Cookies.Append(_cookieName, payload);
		_context.Request.Headers.Cookie = CookiePair(scratch.Response.Headers.SetCookie[0]!);
		return this;
	}

	static string CookiePair(string setCookieHeader) => setCookieHeader[..setCookieHeader.IndexOf(';')];
}
