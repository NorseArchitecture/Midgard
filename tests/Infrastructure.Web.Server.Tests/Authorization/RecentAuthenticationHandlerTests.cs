using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Norse.Abstractions.Components.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Authentication;
using Norse.Infrastructure.Web.Server.Authorization;

namespace Norse.Infrastructure.Web.Server.Tests.Authorization;

public sealed class RecentAuthenticationHandlerTests
{
	static DateTimeOffset Now => new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

	static TimeSpan Window => TimeSpan.FromMinutes(10);

	[Fact]
	async Task Age_equal_to_the_window_is_fresh()
	{
		(await EvaluateAsync(AuthTimed(Now - Window))).HasSucceeded.ShouldBeTrue();
	}

	[Fact]
	async Task One_second_past_the_window_is_stale()
	{
		ShouldBeStale(await EvaluateAsync(AuthTimed(Now - Window - TimeSpan.FromSeconds(1))));
	}

	[Fact]
	async Task A_future_auth_time_is_stale()
	{
		ShouldBeStale(await EvaluateAsync(AuthTimed(Now + RecentAuthenticationHandler.Skew + TimeSpan.FromSeconds(1))));
		// Within the skew allowance a future auth time is a clock disagreement, not a forgery.
		(await EvaluateAsync(AuthTimed(Now + RecentAuthenticationHandler.Skew))).HasSucceeded.ShouldBeTrue();
	}

	[Fact]
	async Task A_missing_auth_time_is_stale()
	{
		ShouldBeStale(await EvaluateAsync(new(new ClaimsIdentity(NorseLanes.Identity))));
	}

	[Theory]
	[InlineData("1793361600.5")]
	[InlineData("1793361600e0")]
	[InlineData(" 1793361600")]
	[InlineData("+1793361600")]
	[InlineData("2026-10-30T12:00:00Z")]
	[InlineData("")]
	[InlineData("99999999999999999999")]
	async Task A_non_integral_auth_time_is_stale(string value)
	{
		ShouldBeStale(await EvaluateAsync(new(new ClaimsIdentity([new Claim(NorseClaimTypes.AuthTime, value)],
			NorseLanes.Identity))));
	}

	[Fact]
	async Task Two_auth_times_are_stale()
	{
		// Ambiguous input is refused, never resolved by picking one.
		var stamp = Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
		ShouldBeStale(await EvaluateAsync(new(new ClaimsIdentity(
			[new Claim(NorseClaimTypes.AuthTime, stamp), new Claim(NorseClaimTypes.AuthTime, stamp)],
			NorseLanes.Identity))));
	}

	[Theory]
	[InlineData(NorseLanes.Identity)]
	[InlineData(NorseLanes.User)]
	[InlineData(NorseLanes.Machine)]
	async Task It_reads_the_claim_on_any_lane(string lane)
	{
		(await EvaluateAsync(AuthTimed(Now, lane))).HasSucceeded.ShouldBeTrue();
		ShouldBeStale(await EvaluateAsync(AuthTimed(Now - Window - TimeSpan.FromSeconds(1), lane)));
	}

	[Fact]
	void The_window_defaults_to_ten_minutes_and_binds_from_norse_authentication()
	{
		ProviderWith().GetRequiredService<IOptions<RecentAuthenticationOptions>>().Value.RecentWindow
			.ShouldBe(TimeSpan.FromMinutes(10));
		ProviderWith(("Norse:Authentication:RecentWindow", "00:05:00"))
			.GetRequiredService<IOptions<RecentAuthenticationOptions>>().Value.RecentWindow
			.ShouldBe(TimeSpan.FromMinutes(5));
	}

	[Theory]
	[InlineData("00:00:00")]
	[InlineData("-00:01:00")]
	[InlineData("1.00:00:01")]
	void An_out_of_range_window_fails_validation(string window)
	{
		Should.Throw<OptionsValidationException>(() => ProviderWith(("Norse:Authentication:RecentWindow", window))
			.GetRequiredService<IOptions<RecentAuthenticationOptions>>().Value);
	}

	[Fact]
	void A_one_day_window_is_valid()
	{
		ProviderWith(("Norse:Authentication:RecentWindow", "1.00:00:00"))
			.GetRequiredService<IOptions<RecentAuthenticationOptions>>().Value.RecentWindow
			.ShouldBe(TimeSpan.FromDays(1));
	}

	static void ShouldBeStale(AuthorizationHandlerContext context)
	{
		context.HasSucceeded.ShouldBeFalse();
		context.HasFailed.ShouldBeTrue();
		context.FailureReasons.Count().ShouldBe(1);
		context.FailureReasons.Single().ShouldBeOfType<ReauthenticationRequiredReason>()
			.Message.ShouldBe(NorseErrorCodes.ReauthenticationRequired);
	}

	static ClaimsPrincipal AuthTimed(DateTimeOffset authTime, string lane = NorseLanes.Identity) =>
		new(new ClaimsIdentity(
			[new Claim(NorseClaimTypes.AuthTime, authTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))],
			lane));

	static async Task<AuthorizationHandlerContext> EvaluateAsync(ClaimsPrincipal user)
	{
		RecentAuthenticationHandler handler = new(
			Options.Create(new RecentAuthenticationOptions { RecentWindow = Window }), new FakeTimeProvider(Now));
		AuthorizationHandlerContext context = new([new RecentAuthenticationRequirement()], user, null);
		await handler.HandleAsync(context);
		return context;
	}

	// The options come from AddNorseAuthentication(), exactly as a host gets them.
	static ServiceProvider ProviderWith(params (string Key, string Value)[] settings)
	{
		ServiceCollection services = new();
		services
			.AddSingleton<IConfiguration>(new ConfigurationBuilder()
				.AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
				.Build())
			.AddLogging()
			.AddNorseAuthentication();
		return services.BuildServiceProvider();
	}
}
