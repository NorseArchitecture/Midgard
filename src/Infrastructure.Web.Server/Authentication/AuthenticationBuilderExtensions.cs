using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authorization;

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
		///     <c>DefaultSignInScheme</c> alone — Norse never signs anyone in, that stays Identity's job. Also
		///     registers the lane and audience requirement handlers, so every lane policy evaluates against the
		///     lane this call's handlers stamp, and the freshness handler with its window
		///     (<c>Norse:Authentication:RecentWindow</c>, default 10 minutes, validated on start, bound from the host's
		///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" />). The two bearer lanes, <see cref="NorseSchemes.Machine" /> and
		///     <see cref="NorseSchemes.User" />, authenticate <see cref="NorseSchemes.Bearer" />, which this call never
		///     registers: the bearer-validating realm must, and a host without it fails at startup.
		/// </summary>
		/// <returns>The <see cref="AuthenticationBuilder" /> for further chaining.</returns>
		public AuthenticationBuilder AddNorseAuthentication()
		{
			services.TryAddEnumerable([
				ServiceDescriptor.Singleton<IAuthorizationHandler, LaneRequirementHandler>(),
				ServiceDescriptor.Singleton<IAuthorizationHandler, AnyLaneRequirementHandler>(),
				ServiceDescriptor.Singleton<IAuthorizationHandler, AudienceRequirementHandler>(),
				ServiceDescriptor.Singleton<IAuthorizationHandler, RecentAuthenticationHandler>()
			]);
			services.TryAddSingleton(TimeProvider.System);
			services.AddOptions<RecentAuthenticationOptions>()
				.BindConfiguration(RecentAuthenticationOptions.SectionName)
				.Validate(options => options.IsValid,
					$"{RecentAuthenticationOptions.SectionName}:{nameof(RecentAuthenticationOptions.RecentWindow)} must be positive and no longer than one day.")
				.ValidateOnStart();
			services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AuthenticationOptions>, BearerSchemeValidation>());
			services.AddOptions<AuthenticationOptions>().ValidateOnStart();
			return services
				.AddAuthentication(options =>
				{
					options.DefaultScheme = NorseSchemes.Default;
					options.DefaultAuthenticateScheme = NorseSchemes.Default;
					options.DefaultChallengeScheme = NorseSchemes.Default;
					options.DefaultForbidScheme = NorseSchemes.Default;
				})
				.AddPolicyScheme(NorseSchemes.Default, null,
					options => options.ForwardDefaultSelector =
						context => NorseLaneSelector.Select(context.GetEndpoint()))
				.AddScheme<AuthenticationSchemeOptions, NorseBrowserHandler>(NorseSchemes.Browser, null)
				.AddScheme<NorseAnonymousOptions, NorseAnonymousHandler>(NorseSchemes.Anonymous, null)
				.AddScheme<AuthenticationSchemeOptions, NorseGrpcHandler>(NorseSchemes.IdentityCookieOnly, null)
				.AddScheme<NorseBearerLaneOptions, NorseBearerLaneHandler>(NorseSchemes.Machine, null,
					options =>
					{
						options.Lane = NorseLanes.Machine;
						options.Audience = NorseAudiences.Facade;
					})
				.AddScheme<NorseBearerLaneOptions, NorseBearerLaneHandler>(NorseSchemes.User, null,
					options =>
					{
						options.Lane = NorseLanes.User;
						options.Audience = NorseAudiences.User;
					})
				.AddScheme<AuthenticationSchemeOptions, NorseProbeHandler>(NorseSchemes.Probe, null);
		}
	}
}
