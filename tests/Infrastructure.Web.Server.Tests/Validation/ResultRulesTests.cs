using System.Globalization;
using FluentValidation;
using Norse.Infrastructure.Web.Server.Validation;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Validation;

public sealed class ResultRulesTests
{
	public sealed record Sample(Result<int> Required, Result<string>? Optional);

	[Fact]
	void Required_rule_passes_a_success_Result()
	{
		InlineValidator<Sample> validator = [];
		validator.RuleFor(s => s.Required).ResultRequired();

		var result = validator.Validate(new Sample(new Success<int>(42), null));

		result.IsValid.ShouldBeTrue();
	}

	[Fact]
	void Required_rule_fails_a_default_Result_with_the_exact_required_missing_message()
	{
		// The one-message-source condition itself: the rendered text must be *literally equal* to
		// FailureDetail.Render of Parser.ParseRequired<int>(string.Empty, ...)'s own failure — not a
		// hardcoded string that happens to match today.
		InlineValidator<Sample> validator = [];
		validator.RuleFor(s => s.Required).ResultRequired();

		var result = validator.Validate(new Sample(default, null));

		result.IsValid.ShouldBeFalse();
		Parser.ParseRequired<int>(string.Empty, CultureInfo.InvariantCulture).TryGetValue(out Failure requiredMissing).ShouldBeTrue();
		result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(FailureDetail.Render(requiredMissing));
	}

	[Fact]
	void Required_rule_fails_a_Failure_Result_rendering_that_failures_own_detail()
	{
		InlineValidator<Sample> validator = [];
		validator.RuleFor(s => s.Required).ResultRequired();
		Failure malformed = new(ParseFailure.Malformed, "x", "Int32");

		var result = validator.Validate(new Sample(malformed, null));

		result.IsValid.ShouldBeFalse();
		result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(FailureDetail.Render(malformed));
	}

	[Fact]
	void Optional_rule_passes_when_absent()
	{
		InlineValidator<Sample> validator = [];
		validator.RuleFor(s => s.Optional).ResultOptional();

		var result = validator.Validate(new Sample(new Success<int>(1), null));

		result.IsValid.ShouldBeTrue();
	}

	[Fact]
	void Optional_rule_passes_when_present_and_successful()
	{
		InlineValidator<Sample> validator = [];
		validator.RuleFor(s => s.Optional).ResultOptional();

		var result = validator.Validate(new Sample(new Success<int>(1), new Success<string>("hi")));

		result.IsValid.ShouldBeTrue();
	}

	[Fact]
	void Optional_rule_fails_when_present_and_failed()
	{
		InlineValidator<Sample> validator = [];
		validator.RuleFor(s => s.Optional).ResultOptional();
		Failure malformed = new(ParseFailure.Malformed, "x", "String");
		Result<string> failed = malformed;

		var result = validator.Validate(new Sample(new Success<int>(1), failed));

		result.IsValid.ShouldBeFalse();
		result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(FailureDetail.Render(malformed));
	}
}
