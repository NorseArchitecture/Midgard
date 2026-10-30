using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;
using Norse.Infrastructure.Web.Server.Authorization;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

/// <summary>
///     Drives one of the platform's <see cref="AuthenticationHandler{TOptions}" /> implementations directly
///     against a <see cref="DefaultHttpContext" />, without a full ASP.NET Core request pipeline. Built for
///     <see cref="NorseAnonymousHandler" /> (Task 5); extended for <see cref="NorseBrowserHandler" /> (Task 6),
///     which internally delegates to the identity cookie and anonymous schemes via
///     <see cref="HttpContext.RequestServices" /> — so a browser harness wires a minimal
///     <see cref="IAuthenticationService" /> that dispatches to real handler instances for those two schemes;
///     extended again for <see cref="NorseGrpcHandler" /> (Train 4, Task 2.2), whose bearer path reaches the two
///     bearer lanes over a stand-in <see cref="NorseSchemes.Bearer" />.
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

	/// <summary>The identity cookie's configured name. Only available on a composite harness (<see cref="ForBrowser" />, <see cref="ForGrpc" />).</summary>
	public string IdentityCookieName =>
		_identityCookieMonitor?.CurrentValue.Cookie.Name
		?? throw new InvalidOperationException($"{nameof(IdentityCookieName)} is only available on a composite harness.");

	/// <summary>
	///     Builds a harness wired for <see cref="NorseAnonymousHandler" /> against <see cref="NorseSchemes.Anonymous" />.
	/// </summary>
	/// <param name="clock">The handler's clock. Defaults to <see cref="TimeProvider.System" />; pass a
	/// <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c> to control cookie-expiry math precisely.</param>
	public static AuthenticationHarness ForAnonymous(TimeProvider? clock = null)
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
				clock ?? TimeProvider.System);
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
		var siblings = Siblings.Create(bearer: null);
		AuthenticationScheme browserScheme = new(NorseSchemes.Browser, NorseSchemes.Browser, typeof(NorseBrowserHandler));

		return new AuthenticationHarness(browserScheme, siblings.AnonymousOptions.CookieName, siblings.Protection, async (s, ctx) =>
			{
				NorseBrowserHandler handler = new(Monitor(new AuthenticationSchemeOptions()), NullLoggerFactory.Instance,
					UrlEncoder.Default, siblings.IdentityMonitor);
				await handler.InitializeAsync(s, ctx);
				return handler;
			},
			siblings.Dispatch, siblings.IdentityScheme, siblings.IdentityMonitor, https);
	}

	/// <summary>
	///     Builds a harness wired for <see cref="NorseGrpcHandler" /> against <see cref="NorseSchemes.IdentityCookieOnly" />,
	///     over the same real identity cookie and anonymous handlers as <see cref="ForBrowser" />, plus both bearer lanes
	///     (real <see cref="NorseBearerLaneHandler" />s) over a stand-in <see cref="NorseSchemes.Bearer" /> that
	///     authenticates <paramref name="bearer" /> and marks its challenge and forbid. Every call the composite makes
	///     through <see cref="HttpContext.RequestServices" /> is recorded in <see cref="Dispatched" />.
	/// </summary>
	/// <param name="bearer">What <see cref="NorseSchemes.Bearer" /> authenticates; <see langword="null" /> for no result.</param>
	public static AuthenticationHarness ForGrpc(ClaimsPrincipal? bearer = null)
	{
		var siblings = Siblings.Create(bearer);
		AuthenticationScheme grpcScheme = new(NorseSchemes.IdentityCookieOnly, NorseSchemes.IdentityCookieOnly, typeof(NorseGrpcHandler));

		return new AuthenticationHarness(grpcScheme, siblings.AnonymousOptions.CookieName, siblings.Protection, async (s, ctx) =>
			{
				NorseGrpcHandler handler = new(Monitor(new AuthenticationSchemeOptions()), NullLoggerFactory.Instance,
					UrlEncoder.Default, siblings.IdentityMonitor, Monitor(siblings.AnonymousOptions), siblings.Protection);
				await handler.InitializeAsync(s, ctx);
				return handler;
			},
			siblings.Dispatch, siblings.IdentityScheme, siblings.IdentityMonitor);
	}

	/// <summary>
	///     Every <c>(operation, scheme)</c> the composite asked <see cref="HttpContext.RequestServices" />'s
	///     <see cref="IAuthenticationService" /> for, in order — <c>"authenticate"</c>, <c>"challenge"</c> or
	///     <c>"forbid"</c>, nested calls included (a bearer lane authenticating <see cref="NorseSchemes.Bearer" />).
	/// </summary>
	public IReadOnlyList<(string Operation, string Scheme)> Dispatched =>
		_requestServices?.GetRequiredService<DispatchLog>().Calls
		?? throw new InvalidOperationException($"{nameof(Dispatched)} is only available on a composite harness.");

	/// <summary>An identity cookie principal carrying, beside its own identity, a second identity typed <c>Norse.Lane.Machine</c>.</summary>
	public static ClaimsPrincipal TicketWithALaneTypedIdentity() =>
		new([
			new ClaimsIdentity([new Claim(ClaimTypes.Name, "user@example.test")], IdentityConstants.ApplicationScheme),
			new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "8d0c1f52-3c6b-4c47-a3f8-2f0e6c0a9b71")], NorseLanes.Machine)
		]);

	/// <summary>Whether Midgard's lane handler admits <paramref name="principal" /> to <c>RequireLane(lane)</c>.</summary>
	public static async Task<bool> AdmitsAsync(ClaimsPrincipal principal, string lane)
	{
		AuthorizationHandlerContext context = new([new LaneRequirement([lane])], principal, null);
		await new LaneRequirementHandler().HandleAsync(context);
		return context.HasSucceeded;
	}

	/// <summary>Sets the request's <c>Authorization</c> header.</summary>
	public AuthenticationHarness WithAuthorization(string value)
	{
		_context.Request.Headers.Authorization = value;
		return this;
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
	///     Authenticates through the wired handler and assigns the result's principal to
	///     <see cref="HttpContext.User" /> — what the authentication middleware does before authorization runs —
	///     then forbids. A forbid only ever reaches a handler after authorization judged that principal, so a
	///     forbid fact that cares <i>who</i> was forbidden drives it through here.
	/// </summary>
	public async Task ForbidAfterAuthenticatingAsync()
	{
		var result = await AuthenticateAsync();
		_context.User = result.Principal!;
		await ForbidAsync();
	}

	/// <summary>
	///     Shapes the current request: its method, path, and <c>Accept</c> header, on host <c>localhost</c> so a
	///     redirect the cookie handler builds as an absolute URL parses.
	/// </summary>
	public AuthenticationHarness WithRequest(string method, string path, string accept)
	{
		_context.Request.Host = new HostString("localhost");
		_context.Request.Method = method;
		_context.Request.Path = path;
		_context.Request.Headers.Accept = accept;
		return this;
	}

	/// <summary>Sets the endpoint routing selected for the current request.</summary>
	public AuthenticationHarness WithEndpoint(Endpoint endpoint)
	{
		_context.SetEndpoint(endpoint);
		return this;
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
	public AuthenticationHarness WithValidIdentityCookie(string name) =>
		WithIdentityCookie(new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], IdentityConstants.ApplicationScheme)));

	/// <summary>
	///     Appends a valid identity cookie whose ticket carries <paramref name="principal" /> exactly as given, every
	///     identity and its authentication type included — written the same way as <see cref="WithValidIdentityCookie" />.
	/// </summary>
	public AuthenticationHarness WithIdentityCookie(ClaimsPrincipal principal)
	{
		AppendRequestCookie(CookiePair(SignInIdentity(principal, expiresUtc: null)));
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
		scratch.Response.Cookies.Append(_cookieName, payload, new CookieOptions { Secure = true });
		return scratch.Response.Headers.SetCookie[0]!;
	}

	string SignInIdentity(ClaimsPrincipal principal, DateTimeOffset? expiresUtc)
	{
		if (_identityScheme is null || _identityCookieMonitor is null)
			throw new InvalidOperationException($"{nameof(SignInIdentity)} is only available on a composite harness.");

		DefaultHttpContext scratch = new();
		scratch.Request.Scheme = _https ? "https" : "http";

		CookieAuthenticationHandler handler = new(_identityCookieMonitor, NullLoggerFactory.Instance, UrlEncoder.Default);
		handler.InitializeAsync(_identityScheme, scratch).GetAwaiter().GetResult();

		AuthenticationProperties? properties = expiresUtc is null ? null : new AuthenticationProperties { ExpiresUtc = expiresUtc };
		handler.SignInAsync(principal, properties).GetAwaiter().GetResult();

		return scratch.Response.Headers.SetCookie[0]!;
	}

	static IOptionsMonitor<TOptions> Monitor<TOptions>(TOptions options) =>
		new FixedOptionsMonitor<TOptions>(options);

	/// <summary>
	///     One options instance for every name. Hand-rolled rather than substituted: NSubstitute cannot proxy an
	///     <see cref="IOptionsMonitor{TOptions}" /> over an internal options type (<see cref="NorseBearerLaneOptions" />,
	///     <see cref="StubBearerOptions" />), because <c>Microsoft.Extensions.Options</c> is strong-named.
	/// </summary>
	sealed class FixedOptionsMonitor<TOptions>(TOptions options) : IOptionsMonitor<TOptions>
	{
		public TOptions CurrentValue => options;

		public TOptions Get(string? name) => options;

		public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
	}

	/// <summary>
	///     The real handlers a composite reaches through <see cref="HttpContext.RequestServices" />: the identity cookie,
	///     the anonymous handler, both bearer lanes, and a stand-in <see cref="NorseSchemes.Bearer" />.
	/// </summary>
	sealed record Siblings(
		IDataProtectionProvider Protection,
		NorseAnonymousOptions AnonymousOptions,
		AuthenticationScheme IdentityScheme,
		IOptionsMonitor<CookieAuthenticationOptions> IdentityMonitor,
		IServiceProvider Dispatch)
	{
		public static Siblings Create(ClaimsPrincipal? bearer)
		{
			var protection = DataProtectionProvider.Create(ProtectionDiscriminator);

			NorseAnonymousOptions anonymousOptions = new();
			var anonymousMonitor = Monitor(anonymousOptions);
			AuthenticationScheme anonymousScheme = new(NorseSchemes.Anonymous, NorseSchemes.Anonymous, typeof(NorseAnonymousHandler));

			CookieAuthenticationOptions identityOptions = new();
			new PostConfigureCookieAuthenticationOptions(protection).PostConfigure(IdentityConstants.ApplicationScheme, identityOptions);
			var identityMonitor = Monitor(identityOptions);
			AuthenticationScheme identityScheme = new(IdentityConstants.ApplicationScheme, IdentityConstants.ApplicationScheme,
				typeof(CookieAuthenticationHandler));

			var bearerMonitor = Monitor(new StubBearerOptions { Principal = bearer });
			AuthenticationScheme bearerScheme = new(NorseSchemes.Bearer, NorseSchemes.Bearer, typeof(StubBearerHandler));

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

			async Task<IAuthenticationHandler> CreateBearerHandler(HttpContext ctx)
			{
				StubBearerHandler handler = new(bearerMonitor, NullLoggerFactory.Instance, UrlEncoder.Default);
				await handler.InitializeAsync(bearerScheme, ctx);
				return handler;
			}

			static Func<HttpContext, Task<IAuthenticationHandler>> Lane(string scheme, string lane, string audience) =>
				async ctx =>
				{
					NorseBearerLaneHandler handler = new(Monitor(new NorseBearerLaneOptions { Lane = lane, Audience = audience }),
						NullLoggerFactory.Instance, UrlEncoder.Default);
					await handler.InitializeAsync(new(scheme, scheme, typeof(NorseBearerLaneHandler)), ctx);
					return handler;
				};

			Dictionary<string, Func<HttpContext, Task<IAuthenticationHandler>>> factories = new(StringComparer.Ordinal)
			{
				[IdentityConstants.ApplicationScheme] = CreateIdentityHandler,
				[NorseSchemes.Anonymous] = CreateAnonymousHandler,
				[NorseSchemes.Bearer] = CreateBearerHandler,
				[NorseSchemes.Machine] = Lane(NorseSchemes.Machine, NorseLanes.Machine, NorseAudiences.Facade),
				[NorseSchemes.User] = Lane(NorseSchemes.User, NorseLanes.User, NorseAudiences.User)
			};

			DispatchLog log = new();
			ServiceCollection services = new();
			services
				.AddSingleton(log)
				.AddSingleton<IAuthenticationService>(new SchemeDispatchAuthenticationService(factories, log));

			return new(protection, anonymousOptions, identityScheme, identityMonitor, services.BuildServiceProvider());
		}
	}

	/// <summary>The calls <see cref="SchemeDispatchAuthenticationService" /> dispatched, in order.</summary>
	sealed class DispatchLog
	{
		public List<(string Operation, string Scheme)> Calls { get; } = [];
	}

	static string CookiePair(string setCookieHeader) => setCookieHeader[..setCookieHeader.IndexOf(';')];

	static bool IsDelete(string setCookieHeader)
	{
		var separator = setCookieHeader.IndexOf('=');
		return separator >= 0 && setCookieHeader.Length > separator + 1 && setCookieHeader[separator + 1] == ';';
	}

	/// <summary>
	///     A minimal <see cref="IAuthenticationService" /> that dispatches <c>AuthenticateAsync</c>/<c>ChallengeAsync</c>/
	///     <c>ForbidAsync</c> to whichever real handler <paramref name="handlerFactories" /> names for the requested scheme —
	///     enough for <see cref="NorseBrowserHandler" />'s internal <c>Context.AuthenticateAsync</c>/<c>Context.ChallengeAsync</c>/
	///     <c>Context.ForbidAsync</c> calls to reach real handler instances without a full ASP.NET Core authentication pipeline.
	/// </summary>
	sealed class SchemeDispatchAuthenticationService(
		IReadOnlyDictionary<string, Func<HttpContext, Task<IAuthenticationHandler>>> handlerFactories,
		DispatchLog log)
		: IAuthenticationService
	{
		public async Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
		{
			var handler = await Resolve("authenticate", context, scheme);
			return await handler.AuthenticateAsync();
		}

		public async Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
		{
			var handler = await Resolve("challenge", context, scheme);
			await handler.ChallengeAsync(properties);
		}

		public async Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
		{
			var handler = await Resolve("forbid", context, scheme);
			await handler.ForbidAsync(properties);
		}

		public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
			throw new NotSupportedException("Not exercised by the composites' tests.");

		public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
			throw new NotSupportedException("Not exercised by the composites' tests.");

		async Task<IAuthenticationHandler> Resolve(string operation, HttpContext context, string? scheme)
		{
			if (scheme is null || !handlerFactories.TryGetValue(scheme, out var factory))
				throw new InvalidOperationException($"No handler factory registered for scheme '{scheme}'.");
			log.Calls.Add((operation, scheme));
			return await factory(context);
		}
	}
}
