using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     Mints or reads the anonymous identity. Never self-selects: the lane selector (§2.2 layer 1) decides
///     which lane a request is in, and only the browser composite invokes this handler. That is what keeps
///     a facade or gRPC caller from ever being handed a free identity.
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

		if (Request.Cookies.TryGetValue(Options.CookieName, out var payload) && TryUnprotect(payload, out var existing))
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

	bool TryUnprotect(string payload, out Guid id)
	{
		id = Guid.Empty;
		try
		{
			// Guid.Empty is rejected here, not only at PrincipalAccessor.Seed. A protected all-zero payload
			// is well-formed and would authenticate cleanly, then fail at the mediator seam -- an
			// authentication layer must not mint a principal it knows the pipeline will refuse. Treated as
			// absence: fresh mint, overwrite.
			return Guid.TryParse(Protector.Unprotect(payload), out id) && id != Guid.Empty;
		}
		catch (System.Security.Cryptography.CryptographicException)
		{
			// A tampered, truncated, or key-rotated payload is indistinguishable from absence for our
			// purposes: mint fresh and overwrite. Never a failed request -- a hostile cookie must not be
			// able to deny service to the visitor holding it.
			return false;
		}
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
