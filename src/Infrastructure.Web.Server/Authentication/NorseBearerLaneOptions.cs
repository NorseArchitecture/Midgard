using Microsoft.AspNetCore.Authentication;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>A <see cref="NorseBearerLaneHandler" /> registration's lane, and the audience that makes a token its kind.</summary>
sealed class NorseBearerLaneOptions : AuthenticationSchemeOptions
{
	/// <summary>
	///     The lane stamped as the authenticated identity's <see cref="System.Security.Claims.ClaimsIdentity.AuthenticationType" />.
	///     Required: an options type cannot carry the <c>required</c> modifier under <c>AddScheme</c>'s <c>new()</c>
	///     constraint, so <see cref="Validate(string)" /> enforces it.
	/// </summary>
	public string Lane { get; set; } = string.Empty;

	/// <summary>
	///     The platform audience (<c>Norse.Abstractions.Components.Authorization.NorseAudiences</c>) a validated token must
	///     carry to be this lane's kind. Required, for the same reason as <see cref="Lane" />.
	/// </summary>
	public string Audience { get; set; } = string.Empty;

	public override void Validate(string scheme)
	{
		base.Validate(scheme);
		if (string.IsNullOrWhiteSpace(Lane))
			throw new InvalidOperationException($"The {scheme} bearer lane names no lane.");
		if (string.IsNullOrWhiteSpace(Audience))
			throw new InvalidOperationException($"The {scheme} bearer lane names no audience.");
	}
}
