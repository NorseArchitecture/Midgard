using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Norse.Infrastructure.Web.Server.Authentication;

/// <summary>Composition-root wiring for the platform's authentication lanes.</summary>
public static class AuthenticationBuilderExtensions
{
	/// <param name="services">The service collection to configure.</param>
	extension(IServiceCollection services)
	{
		/// <summary>
		///     Registers the lane selector and every lane behind it. Must be called <b>after</b> ASP.NET
		///     Core Identity's <c>AddIdentity</c> in the composition root: <c>AddIdentity</c> sets
		///     <see cref="AuthenticationOptions.DefaultAuthenticateScheme" /> and
		///     <see cref="AuthenticationOptions.DefaultChallengeScheme" /> explicitly, which outrank a bare
		///     <c>DefaultScheme</c> — Options composition is last-registration-wins per property, so this
		///     call sets all three (plus <c>DefaultForbidScheme</c>) itself to win regardless. Leaves
		///     <c>DefaultSignInScheme</c> alone — Norse never signs anyone in, that stays Identity's job.
		/// </summary>
		/// <returns>The <see cref="AuthenticationBuilder" /> for further chaining (Himinbjorg#49 adds bearer).</returns>
		public AuthenticationBuilder AddNorseAuthentication() =>
			services
				.AddAuthentication(options =>
				{
					options.DefaultScheme = NorseSchemes.Default;
					options.DefaultAuthenticateScheme = NorseSchemes.Default;
					options.DefaultChallengeScheme = NorseSchemes.Default;
					options.DefaultForbidScheme = NorseSchemes.Default;
				})
				.AddPolicyScheme(NorseSchemes.Default, NorseSchemes.Default,
					options => options.ForwardDefaultSelector =
						context => NorseLaneSelector.Select(context.GetEndpoint()))
				.AddScheme<AuthenticationSchemeOptions, NorseBrowserHandler>(NorseSchemes.Browser, null)
				.AddScheme<NorseAnonymousOptions, NorseAnonymousHandler>(NorseSchemes.Anonymous, null)
				.AddPolicyScheme(NorseSchemes.IdentityCookieOnly, NorseSchemes.IdentityCookieOnly,
					options =>
					{
						// Authenticate against the identity cookie -- but never inherit its challenge, which
						// is a 302 to a login page. A gRPC client cannot follow a redirect and must not be
						// sent one; both non-authenticate operations go bare.
						options.ForwardAuthenticate = IdentityConstants.ApplicationScheme;
						options.ForwardChallenge = NorseSchemes.Machine;
						options.ForwardForbid = NorseSchemes.Machine;
					})
				.AddScheme<AuthenticationSchemeOptions, NorseMachineRejectionHandler>(NorseSchemes.Machine, null)
				.AddScheme<AuthenticationSchemeOptions, NorseProbeHandler>(NorseSchemes.Probe, null);
	}
}
