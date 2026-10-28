using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Web.Server.Authentication;
using OpenIddict.Validation.AspNetCore;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class AuthenticationBuilderExtensionsTests
{
	[Fact]
	void Wins_the_default_authenticate_and_challenge_schemes_over_a_prior_registration()
	{
		ServiceCollection services = new();
		// Mirrors what AddIdentity<TUser, TRole>() itself does to AuthenticationOptions -- the shape
		// Midgard#76's Codex review flagged: Identity sets these two explicitly, so composition-root
		// ordering plus a bare AddAuthentication(defaultScheme) call cannot be trusted to beat it.
		services.AddAuthentication(o =>
		{
			o.DefaultAuthenticateScheme = "Identity.Application";
			o.DefaultChallengeScheme = "Identity.Application";
			o.DefaultSignInScheme = "Identity.External";
		});

		services.AddNorseAuthentication();

		var options = services.BuildServiceProvider().GetRequiredService<IOptions<AuthenticationOptions>>().Value;
		options.DefaultScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultAuthenticateScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultChallengeScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultForbidScheme.ShouldBe(NorseSchemes.Default);
		// Norse never signs anyone in -- Identity's sign-in scheme is untouched.
		options.DefaultSignInScheme.ShouldBe("Identity.External");
	}

	[Fact]
	void The_machine_scheme_forwards_to_OpenIddicts_validation_scheme()
	{
		ServiceCollection services = new();
		services.AddNorseAuthentication();

		var provider = services.BuildServiceProvider();
		var options = provider.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>().Get(NorseSchemes.Machine);

		options.ForwardDefault.ShouldBe(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
	}

	[Fact]
	async Task Registers_no_scheme_with_a_display_name_so_none_is_offered_as_an_external_login()
	{
		ServiceCollection services = new();
		services.AddNorseAuthentication();

		var schemes = await services.BuildServiceProvider().GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();

		// A display name is what SignInManager.GetExternalAuthenticationSchemesAsync keys on: any Norse lane
		// carrying one would be offered on the ExternalLogins page as a linkable provider.
		schemes.ShouldNotBeEmpty();
		schemes.Where(s => s.DisplayName is not null).Select(s => s.Name).ShouldBeEmpty();
	}
}
