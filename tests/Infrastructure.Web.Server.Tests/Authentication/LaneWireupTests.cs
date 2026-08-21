using System.Net;

namespace Norse.Infrastructure.Web.Server.Tests.Authentication;

public sealed class LaneWireupTests
{
	[Fact]
	async Task A_credentialless_grpc_call_mints_nothing_and_writes_no_cookie()
	{
		using var host = await LaneHost.StartAsync();
		using var body = LaneHost.EmptyGrpcBody();

		var response = await host.Client.PostAsync(new Uri("/probe.ProbeService/Ping", UriKind.Relative), body,
			TestContext.Current.CancellationToken);

		response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
	}

	[Fact]
	async Task A_credentialless_facade_call_is_rejected_before_the_action_runs()
	{
		using var host = await LaneHost.StartAsync();

		var response = await host.Client.GetAsync(new Uri("/api/probe/1", UriKind.Relative),
			TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
		(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
		host.FacadeActionInvocations.ShouldBe(0);
		response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
	}

	[Fact]
	async Task A_browser_request_mints_an_anonymous_cookie()
	{
		using var host = await LaneHost.StartAsync();

		var response = await host.Client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

		response.Headers.GetValues("Set-Cookie")
			.ShouldContain(h => h.StartsWith("Norse.Anonymous=", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("/alive")]
	[InlineData("/health")]
	async Task A_probe_request_succeeds_and_is_handed_no_cookie(string path)
	{
		using var host = await LaneHost.StartAsync();

		var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Headers.TryGetValues("Set-Cookie", out _).ShouldBeFalse();
	}
}
