using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;
#pragma warning disable IDE0005 // Using directive is unnecessary
using Norse.Primitives;
#pragma warning restore IDE0005
#pragma warning disable IDE0005 // Using directive is unnecessary
using Shouldly;
#pragma warning restore IDE0005

namespace Norse.Infrastructure.Web.Server.Tests.Mediator.Grpc;

public sealed class OutcomeExtensionsTests
{
	[Fact]
	void ThrowIfFailed_returns_the_value_on_success()
	{
		var outcome = Outcome<bool>.Ok(true);

		outcome.ThrowIfFailed().ShouldBeTrue();
	}

	[Fact]
	void ThrowIfFailed_throws_OutcomeFailedException_carrying_the_Problem_on_failure()
	{
		var outcome = Outcome<bool>.Err(ErrorCategory.LockedOut);

		var exception = Should.Throw<OutcomeFailedException>(() => outcome.ThrowIfFailed());

		exception.Problem.Category.ShouldBe(ErrorCategory.LockedOut);
	}

	[Fact]
	void Void_success_ThrowIfFailed_does_not_throw_on_success()
	{
		var outcome = Outcome<Unit>.Ok(default);
		Should.NotThrow(() => outcome.ThrowIfFailed());
	}

	[Fact]
	void Void_failure_ThrowIfFailed_throws_OutcomeFailedException_on_failure()
	{
		var outcome = Outcome<Unit>.Err(ErrorCategory.Conflict);

		var exception = Should.Throw<OutcomeFailedException>(() => outcome.ThrowIfFailed());

		exception.Problem.Category.ShouldBe(ErrorCategory.Conflict);
	}
}
