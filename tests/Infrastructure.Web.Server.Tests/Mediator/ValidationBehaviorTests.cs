using FluentValidation;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class ValidationBehaviorTests
{
	[Fact]
	async Task No_registered_validators_means_a_valid_request()
	{
		ValidationBehavior<Sample, bool> behavior = new([]);
		var outcome = await behavior.Handle(new Sample("anything"), () => ValueTask.FromResult(Outcome<bool>.Ok(true)),
			TestContext.Current.CancellationToken);
		outcome.TryGetValue(out Norse.Primitives.Success<bool> success).ShouldBeTrue();
		success.Value.ShouldBeTrue();
	}

	[Fact]
	async Task Multiple_validators_aggregate_failures_by_property()
	{
		InlineValidator<Sample> first = [];
		first.RuleFor(s => s.Name).NotEmpty();
		InlineValidator<Sample> second = [];
		second.RuleFor(s => s.Name).MinimumLength(3).WithMessage("too short");

		ValidationBehavior<Sample, bool> behavior = new([first, second]);
		var outcome = await behavior.Handle(new Sample(""),
			() => throw new InvalidOperationException("must not reach handler"), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Validation);
		failed.Problem.Errors["Name"].Length.ShouldBe(2);
	}

	public sealed record Sample(string Name) : ICommandRequest<bool>;
}
