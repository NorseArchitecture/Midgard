using System.Security.Claims;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Norse.Infrastructure.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class PrincipalSeedingInterceptorTests
{
	static ServerCallContext CreateContext(HttpContext httpContext)
	{
		var context = TestServerCallContext.Create(
			method: "/Test/Method",
			host: "localhost",
			deadline: DateTime.MaxValue,
			requestHeaders: [],
			cancellationToken: CancellationToken.None,
			peer: "127.0.0.1:5000",
			authContext: null,
			contextPropagationToken: null,
			writeHeadersFunc: null,
			writeOptionsGetter: null,
			writeOptionsSetter: null);
		context.UserState["__HttpContext"] = httpContext;
		return context;
	}

	static ClaimsPrincipal Authenticated() =>
		new(new ClaimsIdentity(authenticationType: "test"));

	[Fact]
	async Task Seeds_the_accessor_before_the_continuation_runs_and_passes_the_response_through_unchanged()
	{
		var services = new ServiceCollection().BuildServiceProvider();
		PrincipalAccessor accessor = new(services);
		var user = Authenticated();
		DefaultHttpContext httpContext = new() { User = user };
		PrincipalSeedingInterceptor interceptor = new(accessor);
		object response = new();
		ClaimsPrincipal? seenDuringContinuation = null;

		var result = await interceptor.UnaryServerHandler<string, object>(
			"request",
			CreateContext(httpContext),
			async (_, _) =>
			{
				seenDuringContinuation = await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken);
				return response;
			});

		seenDuringContinuation.ShouldBeSameAs(user);
		result.ShouldBeSameAs(response);
	}
}
