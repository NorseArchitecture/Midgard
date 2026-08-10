using Microsoft.AspNetCore.Mvc.Filters;

namespace Norse.Infrastructure.Web.Server.Mediator;

/// <summary>
///     The controller channel's half of the principal contract (spec §2.4) — the REST twin of the gRPC
///     <see cref="Grpc.PrincipalSeedingInterceptor" />: stamps the request principal into the scoped
///     <see cref="PrincipalAccessor" /> at MVC's earliest filter stage, before any action body can send
///     through the pipeline. Without it an unseeded MVC scope falls through to
///     <c>AuthenticationStateProvider</c> — wrong twice on this channel: absent on a lean API host (loud
///     throw), and circuit-shaped on a Blazor Server host, where it throws outside a Razor component's DI
///     scope and faults every REST facade request (found live on Yggdrasil's composition root,
///     2026-08-10). Registered globally by <c>AddNorsePipeline()</c>; resolves the concrete accessor from
///     the request's own scope and no-ops when a host has swapped its own <c>IPrincipalAccessor</c> in
///     and the concrete type is absent.
/// </summary>
sealed class PrincipalSeedingFilter : IAuthorizationFilter
{
	public void OnAuthorization(AuthorizationFilterContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		context.HttpContext.RequestServices.GetService<PrincipalAccessor>()
			?.Seed(context.HttpContext.User);
	}
}
