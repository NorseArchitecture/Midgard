using Microsoft.AspNetCore.DataProtection;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The anonymous cookie's decode step, factored out so it has exactly one implementation: a codex
///     review on the gRPC lane's first cut (PR #78) found that gating on the cookie's mere presence let a
///     tampered/garbage <c>Norse.Anonymous</c> value reach <see cref="NorseAnonymousHandler" />'s mint path
///     anyway (an unprotect failure there is indistinguishable from absence, by design, for the browser
///     lane) — a credential-less gRPC caller could turn itself into an authenticated one by sending any
///     cookie of that name. <see cref="NorseGrpcHandler" /> now validates a real decode here first, so a
///     bogus payload never reaches the handler that would mint over it.
/// </summary>
static class NorseAnonymousCookieReader
{
	/// <summary>
	///     Decodes <paramref name="payload" /> to the <see cref="Guid" /> it was minted with. Returns
	///     <see langword="false" /> for anything that is not a genuine, still-valid protected payload —
	///     tampered, truncated, key-rotated, or a well-formed-but-all-zero identity — the same standard
	///     <see cref="NorseAnonymousHandler.ReadOrMint" /> already applied to itself.
	/// </summary>
	internal static bool TryUnprotect(IDataProtector protector, string payload, out Guid id)
	{
		id = Guid.Empty;
		try
		{
			return Guid.TryParse(protector.Unprotect(payload), out id) && id != Guid.Empty;
		}
		catch (System.Security.Cryptography.CryptographicException)
		{
			return false;
		}
	}
}
