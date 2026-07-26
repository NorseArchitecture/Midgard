using FluentValidation;
using FluentValidation.Results;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class ValidationBehaviorTests
{
	[Fact]
	async Task Invalid_ReturnsValidationOutcome_GroupedByField()
	{
		var validator = Substitute.For<IValidator<string>>();
		validator.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new ValidationResult([
				new ValidationFailure("Email", "Email is required"),
				new ValidationFailure("Email", "Email is not a valid address"),
				new ValidationFailure("Password", "Password is required"),
			])));
		var behavior = new ValidationBehavior<string, bool>(validator);

		var outcome = await behavior.Handle("request", CancellationToken.None, () => throw new InvalidOperationException("should not reach handler"));

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Validation);
		failed.Problem.Errors["Email"].ShouldBe(["Email is required", "Email is not a valid address"]);
		failed.Problem.Errors["Password"].ShouldBe(["Password is required"]);
	}

	[Fact]
	async Task Valid_CallsNext()
	{
		var validator = Substitute.For<IValidator<string>>();
		validator.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new ValidationResult()));
		var behavior = new ValidationBehavior<string, bool>(validator);

		var outcome = await behavior.Handle("request", CancellationToken.None, () => ValueTask.FromResult(Outcome<bool>.Ok(true)));

		outcome.TryGetValue(out Primitives.Success<bool> _).ShouldBeTrue();
	}
}
