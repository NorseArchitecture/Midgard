using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Components.Authorization;

namespace Norse.Infrastructure.Web.Server.Authorization;

/// <summary>
///     Succeeds <see cref="RecentAuthenticationRequirement" /> only for a principal carrying exactly one
///     <see cref="NorseClaimTypes.AuthTime" />, in integral epoch seconds, no older than
///     <see cref="RecentAuthenticationOptions.RecentWindow" /> and no further ahead of now than <see cref="Skew" />.
///     Reads the claim off any lane, cookie or bearer, and never learns who minted it. Anything else is stale, never
///     absent: it fails with exactly one <see cref="ReauthenticationRequiredReason" />.
/// </summary>
sealed class RecentAuthenticationHandler(IOptions<RecentAuthenticationOptions> options, TimeProvider clock) :
	AuthorizationHandler<RecentAuthenticationRequirement>
{
	/// <summary>
	///     How far ahead of this host's clock an authentication time may sit and still be read as a clock
	///     disagreement rather than a forgery. Midgard's own constant: the minting realm's allowance never crosses
	///     the realm line.
	/// </summary>
	public static TimeSpan Skew { get; } = TimeSpan.FromMinutes(5);

	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
		RecentAuthenticationRequirement requirement)
	{
		if (IsFresh(context))
			context.Succeed(requirement);
		else
			context.Fail(new ReauthenticationRequiredReason(this));
		return Task.CompletedTask;
	}

	bool IsFresh(AuthorizationHandlerContext context)
	{
		// Two auth times are ambiguous; picking one would be guessing.
		var claims = context.User.FindAll(NorseClaimTypes.AuthTime).Take(2).ToList();
		if (claims is not [var claim] ||
			!long.TryParse(claim.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
			seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
			return false;

		var age = clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
		return age >= -Skew && age <= options.Value.RecentWindow;
	}
}
