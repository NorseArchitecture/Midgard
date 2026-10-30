using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     Fails the host at startup, not at the first bearer request, when <see cref="NorseSchemes.Bearer" /> is not
///     registered: both bearer lanes authenticate it by name, and Midgard never implements it (spec §7.1).
///     Registered with <c>ValidateOnStart</c> by <see cref="AuthenticationBuilderExtensions.AddNorseAuthentication" />.
/// </summary>
sealed class BearerSchemeValidation : IValidateOptions<AuthenticationOptions>
{
	public ValidateOptionsResult Validate(string? name, AuthenticationOptions options) =>
		options.SchemeMap.ContainsKey(NorseSchemes.Bearer)
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(
				$"No '{NorseSchemes.Bearer}' authentication scheme is registered. {NorseSchemes.Machine} and " +
				$"{NorseSchemes.User} authenticate it by name; the bearer-validating realm must register it.");
}
