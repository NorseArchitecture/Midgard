using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator.Grpc;

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
	void Non_generic_ThrowIfFailed_does_not_throw_on_success()
	{
		Should.NotThrow(() => Outcome.Ok().ThrowIfFailed());
	}

	[Fact]
	void Non_generic_ThrowIfFailed_throws_OutcomeFailedException_on_failure()
	{
		var outcome = Outcome.Err(ErrorCategory.Conflict);

		var exception = Should.Throw<OutcomeFailedException>(outcome.ThrowIfFailed);

		exception.Problem.Category.ShouldBe(ErrorCategory.Conflict);
	}
}
