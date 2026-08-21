using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Norse.Infrastructure.Web.Server.Authentication;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

/// <summary>
///     Drives one of the platform's <see cref="AuthenticationHandler{TOptions}" /> implementations directly
///     against a <see cref="DefaultHttpContext" />, without a full ASP.NET Core request pipeline. Built for
///     <see cref="NorseAnonymousHandler" /> (Task 5); extended for <see cref="NorseBrowserHandler" /> (Task 6),
///     which internally delegates to the identity cookie and anonymous schemes via
///     <see cref="HttpContext.RequestServices" /> — so a browser harness wires a minimal
///     <see cref="IAuthenticationService" /> that dispatches to real handler instances for those two schemes.
///     <para>
///     A fresh handler instance is created on every <see cref="AuthenticateAsync" />/<see cref="ChallengeAsync" />/
///     <see cref="ForbidAsync" /> call, bound to whatever <see cref="HttpContext" /> is current at that moment —
///     mirroring how a real host resolves a new handler per request rather than reusing one across calls.
///     </para>
/// </summary>
sealed class AuthenticationHarness
{
	/// <summary>
	///     The Data Protection discriminator for every ephemeral keyring this fixture creates. Each
	///     <see cref="DataProtectionProvider.Create(string)" /> call already gets its own isolated, in-memory
	///     keyring regardless of the string passed — this is not a shared purpose scope across harness
	///     instances, only a fixed, meaningful label so the fixture doesn't key itself to whichever test
	///     class happened to call a factory first.
	/// </summary>
	const string ProtectionDiscriminator = nameof(AuthenticationHarness);

	readonly Func<AuthenticationScheme, HttpContext, Task<IAuthenticationHandler>> _createHandler;
	readonly string _cookieName;
	readonly bool _https;
	readonly AuthenticationScheme? _identityScheme;
	readonly IOptionsMonitor<CookieAuthenticationOptions>? _identityCookieMonitor;
	readonly IDataProtectionProvider _protection;
	readonly IServiceProvider? _requestServices;
	readonly AuthenticationScheme _scheme;
	DefaultHttpContext _context = new();

	AuthenticationHarness(AuthenticationScheme scheme, string cookieName, IDataProtectionProvider protection,
		Func<AuthenticationScheme, HttpContext, Task<IAuthenticationHandler>> createHandler,
		IServiceProvider? requestServices = null,
		AuthenticationScheme? identityScheme = null,
		IOptionsMonitor<CookieAuthenticationOptions>? identityCookieMonitor = null,
		bool https = true)
	{
		_scheme = scheme;
		_cookieName = cookieName;
		_protection = protection;
		_createHandler = createHandler;
		_requestServices = requestServices;
		_identityScheme = identityScheme;
		_identityCookieMonitor = identityCookieMonitor;
		_https = https;

		if (requestServices is not null)
			_context.RequestServices = requestServices;
		_context.Request.Scheme = https ? "https" : "http";
	}

	/// <summary>The current request — mutate its headers/cookies before calling <see cref="AuthenticateAsync" />.</summary>
	public HttpRequest Request => _context.Request;

	/// <summary>The current response — inspect status code/headers after <see cref="ChallengeAsync" />/<see cref="ForbidAsync" />.</summary>
	public HttpResponse Response => _context.Response;

	/// <summary>The current response's raw <c>Set-Cookie</c> header values.</summary>
	public IReadOnlyList<string> SetCookies => [.. _context.Response.Headers.SetCookie.OfType<string>()];

	/// <summary>Names of every cookie the response asked the browser to delete (an empty-valued, expired <c>Set-Cookie</c>).</summary>
	public IReadOnlyList<string> DeletedCookies =>
		[.. SetCookies.Where(IsDelete).Select(header => header[..header.IndexOf('=')])];

	/// <summary>The identity cookie's configured name. Only available on a harness built via <see cref="ForBrowser" />.</summary>
	public string IdentityCookieName =>
		_identityCookieMonitor?.CurrentValue.Cookie.Name
		?? throw new InvalidOperationException($"{nameof(IdentityCookieName)} is only available on a harness built via {nameof(ForBrowser)}.");

	/// <summary>Builds a harness wired for <see cref="NorseAnonymousHandler" /> against <see cref="NorseSchemes.Anonymous" />.</summary>
	public static AuthenticationHarness ForAnonymous()
	{
		NorseAnonymousOptions options = new();
		var monitor = Substitute.For<IOptionsMonitor<NorseAnonymousOptions>>();
		monitor.CurrentValue.Returns(options);
		monitor.Get(Arg.Any<string>()).Returns(options);

		var protection = DataProtectionProvider.Create(ProtectionDiscriminator);
		AuthenticationScheme scheme = new(NorseSchemes.Anonymous, NorseSchemes.Anonymous, typeof(NorseAnonymousHandler));

		return new AuthenticationHarness(scheme, options.CookieName, protection, async (s, ctx) =>
		{
			NorseAnonymousHandler handler = new(monitor, NullLoggerFactory.Instance, UrlEncoder.Default, protection,
				TimeProvider.System);
			await handler.InitializeAsync(s, ctx);
			return handler;
		});
	}

	/// <summary>
	///     Builds a harness wired for <see cref="NorseBrowserHandler" /> against <see cref="NorseSchemes.Browser" />.
	///     The identity cookie scheme (a real <see cref="CookieAuthenticationHandler" />, configured the same way
	///     <c>PostConfigureCookieAuthenticationOptions</c> configures it in production) and the anonymous scheme
	///     (<see cref="NorseAnonymousHandler" />) are both registered on the returned context's
	///     <see cref="HttpContext.RequestServices" />, so the composite's internal
	///     <c>Context.AuthenticateAsync</c>/<c>Context.ChallengeAsync</c> delegation resolves against real handlers.
	/// </summary>
	/// <param name="https">Whether the simulated request arrived over HTTPS — drives <see cref="CookieSecurePolicy.SameAsRequest" />.</param>
	public static AuthenticationHarness ForBrowser(bool https = true)
	{
		var protection = DataProtectionProvider.Create(ProtectionDiscriminator);

		NorseAnonymousOptions anonymousOptions = new();
		var anonymousMonitor = Substitute.For<IOptionsMonitor<NorseAnonymousOptions>>();
		anonymousMonitor.CurrentValue.Returns(anonymousOptions);
		anonymousMonitor.Get(Arg.Any<string>()).Returns(anonymousOptions);
		AuthenticationScheme anonymousScheme = new(NorseSchemes.Anonymous, NorseSchemes.Anonymous, typeof(NorseAnonymousHandler));

		CookieAuthenticationOptions identityOptions = new();
		new PostConfigureCookieAuthenticationOptions(protection).PostConfigure(IdentityConstants.ApplicationScheme, identityOptions);
		var identityMonitor = Substitute.For<IOptionsMonitor<CookieAuthenticationOptions>>();
		identityMonitor.CurrentValue.Returns(identityOptions);
		identityMonitor.Get(Arg.Any<string>()).Returns(identityOptions);
		AuthenticationScheme identityScheme = new(IdentityConstants.ApplicationScheme, IdentityConstants.ApplicationScheme,
			typeof(CookieAuthenticationHandler));

		AuthenticationSchemeOptions browserOptions = new();
		var browserMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
		browserMonitor.CurrentValue.Returns(browserOptions);
		browserMonitor.Get(Arg.Any<string>()).Returns(browserOptions);
		AuthenticationScheme browserScheme = new(NorseSchemes.Browser, NorseSchemes.Browser, typeof(NorseBrowserHandler));

		async Task<IAuthenticationHandler> CreateIdentityHandler(HttpContext ctx)
		{
			CookieAuthenticationHandler handler = new(identityMonitor, NullLoggerFactory.Instance, UrlEncoder.Default);
			await handler.InitializeAsync(identityScheme, ctx);
			return handler;
		}

		async Task<IAuthenticationHandler> CreateAnonymousHandler(HttpContext ctx)
		{
			NorseAnonymousHandler handler = new(anonymousMonitor, NullLoggerFactory.Instance, UrlEncoder.Default, protection,
				TimeProvider.System);
			await handler.InitializeAsync(anonymousScheme, ctx);
			return handler;
		}

		Dictionary<string, Func<HttpContext, Task<IAuthenticationHandler>>> siblings = new(StringComparer.Ordinal)
		{
			[IdentityConstants.ApplicationScheme] = CreateIdentityHandler,
			[NorseSchemes.Anonymous] = CreateAnonymousHandler
		};

		ServiceCollection services = new();
		services.AddSingleton<IAuthenticationService>(new SchemeDispatchAuthenticationService(siblings));

		return new AuthenticationHarness(browserScheme, anonymousOptions.CookieName, protection, async (s, ctx) =>
			{
				NorseBrowserHandler handler = new(browserMonitor, NullLoggerFactory.Instance, UrlEncoder.Default, identityMonitor);
				await handler.InitializeAsync(s, ctx);
				return handler;
			},
			services.BuildServiceProvider(), identityScheme, identityMonitor, https);
	}

	/// <summary>Initializes and invokes the wired handler's <see cref="IAuthenticationHandler.AuthenticateAsync" />.</summary>
	public async Task<AuthenticateResult> AuthenticateAsync()
	{
		var handler = await _createHandler(_scheme, _context);
		return await handler.AuthenticateAsync();
	}

	/// <summary>Initializes and invokes the wired handler's <see cref="IAuthenticationHandler.ChallengeAsync" />.</summary>
	public async Task ChallengeAsync()
	{
		var handler = await _createHandler(_scheme, _context);
		await handler.ChallengeAsync(properties: null);
	}

	/// <summary>Initializes and invokes the wired handler's <see cref="IAuthenticationHandler.ForbidAsync" />.</summary>
	public async Task ForbidAsync()
	{
		var handler = await _createHandler(_scheme, _context);
		await handler.ForbidAsync(properties: null);
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
		if (_requestServices is not null)
			_context.RequestServices = _requestServices;
		_context.Request.Scheme = _https ? "https" : "http";
		if (pairs.Length > 0)
			_context.Request.Headers.Cookie = new StringValues(pairs);
	}

	/// <summary>
	///     Sets the request's cookie header to a Data-Protection-protected payload for <paramref name="id" />,
	///     encoded exactly as <see cref="NorseAnonymousHandler" /> itself would write it. Overwrites any cookie
	///     header already present.
	/// </summary>
	public AuthenticationHarness WithProtectedAnonymousPayload(Guid id)
	{
		_context.Request.Headers.Cookie = CookiePair(BuildAnonymousCookie(id));
		return this;
	}

	/// <summary>
	///     Appends a Data-Protection-protected anonymous cookie for <paramref name="id" /> onto the request,
	///     alongside whatever cookies are already present (e.g. an identity cookie set by
	///     <see cref="WithValidIdentityCookie" />).
	/// </summary>
	public AuthenticationHarness WithAnonymousCookie(Guid id)
	{
		AppendRequestCookie(CookiePair(BuildAnonymousCookie(id)));
		return this;
	}

	/// <summary>
	///     Appends a valid identity cookie — written through a real <see cref="CookieAuthenticationHandler" />'s
	///     <c>SignInAsync</c>, so the ticket is encoded exactly as production would encode it — carrying
	///     <see cref="ClaimTypes.Name" /> = <paramref name="name" />.
	/// </summary>
	public AuthenticationHarness WithValidIdentityCookie(string name)
	{
		ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, name)], IdentityConstants.ApplicationScheme);
		AppendRequestCookie(CookiePair(SignInIdentity(new ClaimsPrincipal(identity), expiresUtc: null)));
		return this;
	}

	/// <summary>
	///     Appends an identity cookie whose ticket already expired — present on the request, but
	///     <see cref="CookieAuthenticationHandler" /> rejects it on read. <paramref name="securePolicy" />
	///     overrides the cookie's <see cref="CookieSecurePolicy" /> for both this write and the eventual delete,
	///     since both go through the same shared options instance.
	/// </summary>
	public AuthenticationHarness WithExpiredIdentityCookie(CookieSecurePolicy? securePolicy = null)
	{
		if (securePolicy is not null)
			_identityCookieMonitor!.CurrentValue.Cookie.SecurePolicy = securePolicy.Value;

		ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, "expired@example.test")], IdentityConstants.ApplicationScheme);
		var expiresUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
		AppendRequestCookie(CookiePair(SignInIdentity(new ClaimsPrincipal(identity), expiresUtc)));
		return this;
	}

	void AppendRequestCookie(string cookiePair) => _context.Request.Headers.Append("Cookie", cookiePair);

	string BuildAnonymousCookie(Guid id)
	{
		var protector = _protection.CreateProtector(NorseAnonymousOptions.ProtectionPurpose);
		var payload = protector.Protect(id.ToString("D"));

		DefaultHttpContext scratch = new();
		scratch.Response.Cookies.Append(_cookieName, payload);
		return scratch.Response.Headers.SetCookie[0]!;
	}

	string SignInIdentity(ClaimsPrincipal principal, DateTimeOffset? expiresUtc)
	{
		if (_identityScheme is null || _identityCookieMonitor is null)
			throw new InvalidOperationException($"{nameof(SignInIdentity)} is only available on a harness built via {nameof(ForBrowser)}.");

		DefaultHttpContext scratch = new();
		scratch.Request.Scheme = _https ? "https" : "http";

		CookieAuthenticationHandler handler = new(_identityCookieMonitor, NullLoggerFactory.Instance, UrlEncoder.Default);
		handler.InitializeAsync(_identityScheme, scratch).GetAwaiter().GetResult();

		AuthenticationProperties? properties = expiresUtc is null ? null : new AuthenticationProperties { ExpiresUtc = expiresUtc };
		handler.SignInAsync(principal, properties).GetAwaiter().GetResult();

		return scratch.Response.Headers.SetCookie[0]!;
	}

	static string CookiePair(string setCookieHeader) => setCookieHeader[..setCookieHeader.IndexOf(';')];

	static bool IsDelete(string setCookieHeader)
	{
		var separator = setCookieHeader.IndexOf('=');
		return separator >= 0 && setCookieHeader.Length > separator + 1 && setCookieHeader[separator + 1] == ';';
	}

	/// <summary>
	///     A minimal <see cref="IAuthenticationService" /> that dispatches <c>AuthenticateAsync</c>/<c>ChallengeAsync</c>
	///     to whichever real handler <paramref name="handlerFactories" /> names for the requested scheme — enough
	///     for <see cref="NorseBrowserHandler" />'s internal <c>Context.AuthenticateAsync</c>/<c>Context.ChallengeAsync</c>
	///     calls to reach real handler instances without a full ASP.NET Core authentication pipeline.
	/// </summary>
	sealed class SchemeDispatchAuthenticationService(IReadOnlyDictionary<string, Func<HttpContext, Task<IAuthenticationHandler>>> handlerFactories)
		: IAuthenticationService
	{
		public async Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
		{
			var handler = await Resolve(context, scheme);
			return await handler.AuthenticateAsync();
		}

		public async Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
		{
			var handler = await Resolve(context, scheme);
			await handler.ChallengeAsync(properties);
		}

		public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
			throw new NotSupportedException("Not exercised by the browser composite's tests.");

		public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
			throw new NotSupportedException("Not exercised by the browser composite's tests.");

		public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
			throw new NotSupportedException("Not exercised by the browser composite's tests.");

		async Task<IAuthenticationHandler> Resolve(HttpContext context, string? scheme)
		{
			if (scheme is null || !handlerFactories.TryGetValue(scheme, out var factory))
				throw new InvalidOperationException($"No handler factory registered for scheme '{scheme}'.");
			return await factory(context);
		}
	}
}
