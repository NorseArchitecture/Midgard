using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Components.Authorization;
using Norse.Infrastructure.Web.Server.Authentication;

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

		services.AddNorseAuthentication().AddScheme<AuthenticationSchemeOptions, BearerStandInHandler>(NorseSchemes.Bearer, null);

		var options = services.BuildServiceProvider().GetRequiredService<IOptions<AuthenticationOptions>>().Value;
		options.DefaultScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultAuthenticateScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultChallengeScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultForbidScheme.ShouldBe(NorseSchemes.Default);
		// Norse never signs anyone in -- Identity's sign-in scheme is untouched.
		options.DefaultSignInScheme.ShouldBe("Identity.External");
	}

	[Theory]
	[InlineData(NorseSchemes.Machine, NorseLanes.Machine, NorseAudiences.Facade)]
	[InlineData(NorseSchemes.User, NorseLanes.User, NorseAudiences.User)]
	async Task Each_bearer_lane_is_a_norse_bearer_lane_handler_with_its_lane_and_audience(string scheme, string lane,
		string audience)
	{
		await using var provider = WithBearerStub().BuildServiceProvider();

		var registered = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(scheme);
		registered.ShouldNotBeNull().HandlerType.ShouldBe(typeof(NorseBearerLaneHandler));
		var options = provider.GetRequiredService<IOptionsMonitor<NorseBearerLaneOptions>>().Get(scheme);
		options.Lane.ShouldBe(lane);
		options.Audience.ShouldBe(audience);
	}

	[Fact]
	void Never_registers_norse_bearer_itself()
	{
		ServiceCollection services = new();
		services.AddNorseAuthentication();
		using var provider = services.BuildServiceProvider();

		Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value)
			.Message.ShouldContain(NorseSchemes.Bearer);
	}

	[Fact]
	async Task Registers_no_scheme_with_a_display_name_so_none_is_offered_as_an_external_login()
	{
		var schemes = await WithBearerStub().BuildServiceProvider().GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();

		// A display name is what SignInManager.GetExternalAuthenticationSchemesAsync keys on: any Norse lane
		// carrying one would be offered on the ExternalLogins page as a linkable provider.
		schemes.ShouldNotBeEmpty();
		schemes.Where(s => s.DisplayName is not null).Select(s => s.Name).ShouldBeEmpty();
	}

	// The bearer source is the bearer-validating realm's to register; these facts stand in for it.
	static ServiceCollection WithBearerStub()
	{
		ServiceCollection services = new();
		services.AddNorseAuthentication().AddScheme<AuthenticationSchemeOptions, BearerStandInHandler>(NorseSchemes.Bearer, null);
		return services;
	}
}
