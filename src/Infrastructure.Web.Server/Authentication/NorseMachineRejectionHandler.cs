using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The machine lane's handler until Himinbjorg#49 lands bearer. Registered rather than left dangling:
///     forwarding to an unregistered scheme name throws a handler-lookup exception, which surfaces as a 500
///     and reads like a server fault instead of the clean 401 a credentialless facade caller must get.
///     Authenticates nothing, challenges silently, never writes a cookie. #49 repoints
///     <see cref="NorseSchemes.Machine" /> at bearer and deletes this type.
/// </summary>
sealed class NorseMachineRejectionHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
		Task.FromResult(AuthenticateResult.NoResult());

	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status401Unauthorized;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}

	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status403Forbidden;
		Response.ContentLength = 0;
		return Task.CompletedTask;
	}
}
