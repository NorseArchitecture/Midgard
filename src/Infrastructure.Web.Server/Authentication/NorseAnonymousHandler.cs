using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     Mints or reads the anonymous identity. Never self-selects: the lane selector (§2.2 layer 1) decides
///     which lane a request is in, and only the browser and gRPC composites invoke this handler — the
///     facade (machine) lane never does, so a bearer-only caller is never handed a free identity. The two
///     composites that do invoke it differ in how: the browser composite (<see cref="NorseBrowserHandler" />)
///     calls it unconditionally, so it is the only lane that can ever mint; the gRPC composite
///     (<see cref="NorseGrpcHandler" />) validates the cookie decodes to a real identity first
///     (<see cref="NorseAnonymousCookieReader" />) before ever calling in, so calling it there can only
///     ever read one the browser lane already minted — never a bogus cookie of the right name.
/// </summary>
sealed class NorseAnonymousHandler(
	IOptionsMonitor<NorseAnonymousOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IDataProtectionProvider protection,
	TimeProvider clock)
	: AuthenticationHandler<NorseAnonymousOptions>(options, logger, encoder)
{
	IDataProtector Protector => protection.CreateProtector(NorseAnonymousOptions.ProtectionPurpose);

	protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
		Task.FromResult(AuthenticateResult.Success(ReadOrMint()));

	AuthenticationTicket ReadOrMint()
	{
		var now = clock.GetUtcNow();

		if (Request.Cookies.TryGetValue(Options.CookieName, out var payload) &&
			NorseAnonymousCookieReader.TryUnprotect(Protector, payload, out var existing))
		{
			// The lifetime is documented as sliding: an active visitor's cookie must not expire out from
			// under them, so every successful read reissues it with a fresh now + Lifetime expiry rather
			// than only the mint path writing one.
			Response.Cookies.Append(Options.CookieName, payload, Options.BuildCookieOptions(now));
			return Ticket(existing);
		}

		var minted = Guid.NewGuid();
		Response.Cookies.Append(Options.CookieName, Protector.Protect(minted.ToString("D")),
			Options.BuildCookieOptions(now));
		return Ticket(minted);
	}

	AuthenticationTicket Ticket(Guid id)
	{
		ClaimsIdentity identity = new(
			[
				new Claim(ClaimTypes.NameIdentifier, id.ToString("D")),
				new Claim(ClaimTypes.Role, NorseAnonymousOptions.AnonymousRole)
			],
			Scheme.Name);
		return new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
	}
}
