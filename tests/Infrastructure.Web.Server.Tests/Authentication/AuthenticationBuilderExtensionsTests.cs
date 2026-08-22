using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

		services.AddNorseAuthentication();

		var options = services.BuildServiceProvider().GetRequiredService<IOptions<AuthenticationOptions>>().Value;
		options.DefaultScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultAuthenticateScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultChallengeScheme.ShouldBe(NorseSchemes.Default);
		options.DefaultForbidScheme.ShouldBe(NorseSchemes.Default);
		// Norse never signs anyone in -- Identity's sign-in scheme is untouched.
		options.DefaultSignInScheme.ShouldBe("Identity.External");
	}
}
