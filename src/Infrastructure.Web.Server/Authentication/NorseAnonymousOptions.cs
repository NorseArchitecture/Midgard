using Microsoft.AspNetCore.Authentication;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The anonymous cookie's protocol (design §2.3). <see cref="CookieOptions.IsEssential" /> is deliberately
///     true: this cookie carries identity, not tracking, so it sits outside consent gating.
/// </summary>
public sealed class NorseAnonymousOptions : AuthenticationSchemeOptions
{
	/// <summary>The role every anonymous principal carries.</summary>
	public const string AnonymousRole = "anonymous";

	/// <summary>The Data Protection purpose string. Versioned so a format change is a new purpose, not a silent reinterpretation.</summary>
	public const string ProtectionPurpose = "Norse.Anonymous.v1";

	/// <summary>Cookie name — never <c>.AspNetCore.*</c>, matching the identity cookie's de-fingerprinting posture.</summary>
	public string CookieName { get; set; } = "Norse.Anonymous";

	/// <summary>Sliding lifetime, 30 days per the 2026-06-07 auth design §12.</summary>
	public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(30);

	/// <summary>Builds the cookie options used for both writing and deleting — one source, so a delete always matches its write.</summary>
	public CookieOptions BuildCookieOptions(DateTimeOffset now) =>
		new()
		{
			HttpOnly = true,
			Secure = true,
			SameSite = SameSiteMode.Lax,
			Path = "/",
			IsEssential = true,
			Expires = now.Add(Lifetime)
		};
}
