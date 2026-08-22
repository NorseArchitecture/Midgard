using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>
///     The orchestrator-probe lane. Authenticates nothing and writes nothing: a liveness probe arrives with
///     no credentials and must not be handed an identity for its trouble. Exists as its own lane because
///     naming <c>NorsePolicies.Probe</c> governs authorization only — it does not stop authentication from
///     running, so without this a probe would fall through to the browser composite and collect a cookie.
/// </summary>
sealed class NorseProbeHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
		Task.FromResult(AuthenticateResult.NoResult());
}
