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
		///     Registers the lane selector and every lane behind it. Deliberately sets <b>no</b> default
		///     scheme beyond the selector: an endpoint that declares nothing gets no principal rather than
		///     the wrong one, which is §2.7's preference order applied to authentication.
		/// </summary>
		/// <returns>The <see cref="AuthenticationBuilder" /> for further chaining (Himinbjorg#49 adds bearer).</returns>
		public AuthenticationBuilder AddNorseAuthentication() =>
			services
				.AddAuthentication(NorseSchemes.Default)
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
