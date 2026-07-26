using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class AuthorizationBehaviorTests
{
	[Fact]
	async Task NotAuthenticated_ReturnsUnauthorized()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity()); // IsAuthenticated: false
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Public").Returns(AuthorizationResult.Failed());

		var behavior = new AuthorizationBehavior<string, bool>("AuthN.Public", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", CancellationToken.None, () => throw new InvalidOperationException("should not reach handler"));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Unauthorized);
	}

	[Fact]
	async Task AuthenticatedButLacksPolicy_ReturnsForbidden()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie")); // IsAuthenticated: true
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Admin").Returns(AuthorizationResult.Failed());

		var behavior = new AuthorizationBehavior<string, bool>("AuthN.Admin", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", CancellationToken.None, () => throw new InvalidOperationException("should not reach handler"));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Forbidden);
	}

	[Fact]
	async Task Authorized_CallsNext()
	{
		var user = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "cookie"));
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Public").Returns(AuthorizationResult.Success());

		var behavior = new AuthorizationBehavior<string, bool>("AuthN.Public", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", CancellationToken.None, () => ValueTask.FromResult(Outcome<bool>.Ok(true)));

		outcome.TryGetValue(out Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}
}
