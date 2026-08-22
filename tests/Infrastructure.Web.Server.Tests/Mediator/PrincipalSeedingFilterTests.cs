using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

/// <summary>
///     The controller channel's half of the principal contract — the REST twin of the gRPC
///     <c>PrincipalSeedingInterceptor</c>. Found live on Yggdrasil's composition root (2026-08-10):
///     an unseeded MVC scope fell through to the circuit-shaped <c>AuthenticationStateProvider</c>,
///     which throws outside a Razor component's DI scope, faulting every REST facade request.
/// </summary>
public sealed class PrincipalSeedingFilterTests
{
	static AuthorizationFilterContext ContextFor(IServiceProvider services, ClaimsPrincipal user)
	{
		DefaultHttpContext httpContext = new() { RequestServices = services, User = user };
		ActionContext actionContext = new(httpContext, new RouteData(), new ActionDescriptor());
		return new AuthorizationFilterContext(actionContext, []);
	}

	[Fact]
	async Task Seeds_the_request_principal_into_the_scoped_accessor_before_the_action_runs()
	{
		// No AuthenticationStateProvider registered anywhere -- the exact real-host REST shape. The
		// accessor must answer with the request's own principal, never fall through or throw.
		var services = new ServiceCollection();
		services.AddNorsePipeline();
		using var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))], "test"));

		new PrincipalSeedingFilter().OnAuthorization(ContextFor(scope.ServiceProvider, user));

		var accessor = scope.ServiceProvider.GetRequiredService<IPrincipalAccessor>();
		(await accessor.GetPrincipalAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(user);
	}

	[Fact]
	void A_scope_without_the_concrete_accessor_is_left_alone()
	{
		// A host that swapped its own IPrincipalAccessor in (every test fixture does) may not carry
		// the concrete type at all -- the filter no-ops rather than demanding it.
		using var provider = new ServiceCollection().BuildServiceProvider();
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));

		Should.NotThrow(() => new PrincipalSeedingFilter().OnAuthorization(ContextFor(provider, user)));
	}

	[Fact]
	void AddNorsePipeline_registers_the_filter_globally_for_the_controller_channel()
	{
		var services = new ServiceCollection();
		services.AddOptions();
		services.AddNorsePipeline();
		using var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredService<IOptions<MvcOptions>>().Value;

		options.Filters.OfType<PrincipalSeedingFilter>().ShouldHaveSingleItem();
	}
}
