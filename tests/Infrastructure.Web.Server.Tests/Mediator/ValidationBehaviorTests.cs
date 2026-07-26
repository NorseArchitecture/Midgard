using FluentValidation;
using FluentValidation.Results;
using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Server.Mediator;
using Norse.Primitives;

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
				new("Email", "Email is not a valid address"),
				new("Password", "Password is required"),
			])));
		ValidationBehavior<string, bool> behavior = new(validator);

		var outcome = await behavior.Handle("request", () => throw new InvalidOperationException("should not reach handler"), TestContext.Current.CancellationToken);

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
		ValidationBehavior<string, bool> behavior = new(validator);

		var outcome = await behavior.Handle("request", () => ValueTask.FromResult(Outcome<bool>.Ok(true)), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Success<bool> _).ShouldBeTrue();
	}
}
