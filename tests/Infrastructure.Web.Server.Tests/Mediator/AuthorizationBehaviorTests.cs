using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class AuthorizationBehaviorTests
{
	[Fact]
	async Task NotAuthenticated_ReturnsUnauthorized()
	{
		ClaimsPrincipal user = new(new ClaimsIdentity()); // IsAuthenticated: false
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Public").Returns(AuthorizationResult.Failed());

		AuthorizationBehavior<string, bool> behavior = new("AuthN.Public", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", () => throw new InvalidOperationException("should not reach handler"), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Unauthorized);
	}

	[Fact]
	async Task AuthenticatedButLacksPolicy_ReturnsForbidden()
	{
		ClaimsPrincipal user = new(new ClaimsIdentity(authenticationType: "cookie")); // IsAuthenticated: true
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Admin").Returns(AuthorizationResult.Failed());

		AuthorizationBehavior<string, bool> behavior = new("AuthN.Admin", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", () => throw new InvalidOperationException("should not reach handler"), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Forbidden);
	}

	[Fact]
	async Task Authorized_CallsNext()
	{
		ClaimsPrincipal user = new(new ClaimsIdentity(authenticationType: "cookie"));
		var authorizationService = Substitute.For<IAuthorizationService>();
		authorizationService.AuthorizeAsync(user, "AuthN.Public").Returns(AuthorizationResult.Success());

		AuthorizationBehavior<string, bool> behavior = new("AuthN.Public", authorizationService, () => ValueTask.FromResult(user));

		var outcome = await behavior.Handle("request", () => ValueTask.FromResult(Outcome<bool>.Ok(true)), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}
}
