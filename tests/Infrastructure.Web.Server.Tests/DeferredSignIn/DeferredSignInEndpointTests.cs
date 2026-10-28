using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Web.Server.DeferredSignIn;
using Norse.Infrastructure.Web.Server.DeferredSignIn;

namespace Norse.Infrastructure.Web.Server.Tests.DeferredSignIn;

/// <summary>
///     Drives <c>/_auth/complete</c> through a real host: the meta refresh lands only on a single-slash-rooted local
///     path, and anything else lands on the PathBase root. A browser strips tab and newline from a meta-refresh URL,
///     so a control character is refused before and after one percent-decode (<c>/\t/evil</c> collapses to
///     <c>//evil</c>).
/// </summary>
public sealed class DeferredSignInEndpointTests
{
	const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

	public static TheoryData<string> Accepted =>
	[
		"/",
		"/Account/Manage",
		"/dashboard?tab=1&x=y",
		"/Account/Manage/ShowRecoveryCodes?handoff=CfDJ8abc-_",
		"/_auth/complete?key=rem&returnUrl=%2F",
		"/_auth/complete?key=rem&returnUrl=%2FAccount%2FManage%2FShowRecoveryCodes%3Fhandoff%3DCfDJ8abc",
	];

	public static TheoryData<string> Refused =>
	[
		"",
		"https://evil.example",
		"evil.example",
		"//evil.example",
		"/\\evil.example",
		"/%2Fevil.example",
		"/%5Cevil.example",
		"/\t/evil.example",
		"/%09/evil.example",
		"/\n/evil.example",
		"/%0A/evil.example",
		"/\r/evil.example",
		"/\u0000/evil.example",
		"/%7F/evil.example",
		"javascript:alert(1)",
	];

	[Theory]
	[MemberData(nameof(Accepted))]
	async Task A_local_return_url_is_the_landing(string returnUrl) =>
		(await CompleteAsync(_ => returnUrl)).ShouldBe(WebUtility.HtmlEncode(returnUrl));

	[Theory]
	[MemberData(nameof(Refused))]
	async Task Anything_else_lands_on_the_root(string returnUrl) =>
		(await CompleteAsync(_ => returnUrl)).ShouldBe("/");

	[Theory]
	[MemberData(nameof(Refused))]
	async Task Anything_else_lands_on_the_path_base_root(string returnUrl) =>
		(await CompleteAsync(_ => returnUrl, "/base")).ShouldBe("/base/");

	[Fact]
	async Task A_chained_completion_built_by_the_store_is_the_landing() =>
		(await CompleteAsync(store => store.BuildCompletionUrl("rem", "/Account/Manage/ShowRecoveryCodes?handoff=abc")))
			.ShouldBe(WebUtility.HtmlEncode("/_auth/complete?key=rem&returnUrl=%2FAccount%2FManage%2FShowRecoveryCodes%3Fhandoff%3Dabc"));

	static async Task<string> CompleteAsync(Func<IDeferredSignIn, string> returnUrl, string pathBase = "")
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.Services
			.AddMemoryCache()
			.AddSingleton<IDeferredSignIn, MemoryCacheDeferredSignIn>()
			.AddAuthentication(Scheme)
			.AddCookie(Scheme);
		await using var app = builder.Build();
		if (pathBase.Length is not 0)
			app.UsePathBase(pathBase);
		app.MapDeferredSignIn();
		await app.StartAsync(TestContext.Current.CancellationToken);

		var store = app.Services.GetRequiredService<IDeferredSignIn>();
		var key = store.StashSignOut(Scheme);
		using var client = app.GetTestClient();
		var response = await client.GetAsync(
			new Uri($"{pathBase}{DeferredSignInEndpointRouteBuilderExtensions.DefaultPattern}?key={key}&returnUrl={Uri.EscapeDataString(returnUrl(store))}",
				UriKind.Relative),
			TestContext.Current.CancellationToken);
		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		const string Marker = "URL=";
		var start = body.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
		return body[start..body.IndexOf('"', start)];
	}
}
