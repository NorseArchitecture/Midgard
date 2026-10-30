using System.Security.Claims;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

/// <summary>
///     Validated bearer identities shaped as OpenIddict validation hands them to Midgard's lanes: the
///     validation scheme's own authentication type, <c>name</c>/<c>role</c> claim types, and one <c>aud</c> claim per
///     audience (a multi-valued <c>aud</c> arrives as several claims). A machine token's subject is its application's
///     <see cref="Guid" /> id; a user token's is the user's.
/// </summary>
static class BearerTokens
{
	public const string AudienceClaimType = "aud";
	public const string MachineSubject = "8d0c1f52-3c6b-4c47-a3f8-2f0e6c0a9b71";
	public const string UserSubject = "user-1";

	const string ValidationType = "OpenIddict.Validation.AspNetCore";

	/// <summary>A client_credentials token; audience <see cref="NorseAudiences.Facade" /> unless told otherwise.</summary>
	public static ClaimsPrincipal Machine(params string[] audiences) =>
		Token(MachineSubject, audiences.Length is 0 ? [NorseAudiences.Facade] : audiences);

	/// <summary>An authorization-code token; audience <see cref="NorseAudiences.User" /> unless told otherwise.</summary>
	public static ClaimsPrincipal User(params string[] audiences) =>
		Token(UserSubject, audiences.Length is 0 ? [NorseAudiences.User] : audiences);

	/// <summary>A validated token carrying no <c>aud</c> claim at all.</summary>
	public static ClaimsPrincipal WithoutAudience() => Token(UserSubject, []);

	static ClaimsPrincipal Token(string subject, string[] audiences) =>
		new(new ClaimsIdentity(
			[
				new Claim("iss", "https://localhost/"),
				new Claim("sub", subject),
				new Claim(ClaimTypes.NameIdentifier, subject),
				new Claim("client_id", "norse-client"),
				new Claim("oi_tkn_id", "token-1"),
				.. audiences.Select(static audience => new Claim(AudienceClaimType, audience))
			],
			ValidationType, "name", "role"));
}
