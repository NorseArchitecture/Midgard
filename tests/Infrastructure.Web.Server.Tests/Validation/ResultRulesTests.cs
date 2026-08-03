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

	public enum Status
	{
		Active = 1,
		Inactive = 2
	}

	public sealed record EnumSample(Result<Status> Required, Result<Status>? Optional);

	[Fact]
	void ResultRequiredEnum_passes_a_success_Result()
	{
		InlineValidator<EnumSample> validator = [];
		validator.RuleFor(s => s.Required).ResultRequiredEnum();

		var result = validator.Validate(new EnumSample(new Success<Status>(Status.Active), null));

		result.IsValid.ShouldBeTrue();
	}

	[Fact]
	void ResultRequiredEnum_fails_a_default_Result_with_the_exact_required_missing_message()
	{
		// One-message-source condition, enum leg: the rendered text must be literally equal to
		// FailureDetail.Render of a directly constructed ParseFailure.Empty failure — enums have no
		// Parser.ParseRequired route (not ISpanParsable), so this is the enum twin's own construction,
		// not a call into the scalar path — yet the wording is byte-identical by construction, since
		// FailureDetail.Render dispatches on ParseFailure.Empty alone, ignoring Input/ExpectedType.
		InlineValidator<EnumSample> validator = [];
		validator.RuleFor(s => s.Required).ResultRequiredEnum();

		var result = validator.Validate(new EnumSample(default, null));

		result.IsValid.ShouldBeFalse();
		result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(FailureDetail.Render(new Failure(ParseFailure.Empty, "", nameof(Status))));
	}

	[Fact]
	void ResultRequiredEnum_fails_a_Failure_Result_rendering_that_failures_own_detail()
	{
		InlineValidator<EnumSample> validator = [];
		validator.RuleFor(s => s.Required).ResultRequiredEnum();
		Failure malformed = new(ParseFailure.Malformed, "x", nameof(Status));

		var result = validator.Validate(new EnumSample(malformed, null));

		result.IsValid.ShouldBeFalse();
		result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(FailureDetail.Render(malformed));
	}

	[Fact]
	void ResultOptionalEnum_passes_when_absent()
	{
		InlineValidator<EnumSample> validator = [];
		validator.RuleFor(s => s.Optional).ResultOptionalEnum();

		var result = validator.Validate(new EnumSample(new Success<Status>(Status.Active), null));

		result.IsValid.ShouldBeTrue();
	}

	[Fact]
	void ResultOptionalEnum_passes_when_present_and_successful()
	{
		InlineValidator<EnumSample> validator = [];
		validator.RuleFor(s => s.Optional).ResultOptionalEnum();

		var result = validator.Validate(new EnumSample(new Success<Status>(Status.Active), new Success<Status>(Status.Inactive)));

		result.IsValid.ShouldBeTrue();
	}

	[Fact]
	void ResultOptionalEnum_fails_when_present_and_failed()
	{
		InlineValidator<EnumSample> validator = [];
		validator.RuleFor(s => s.Optional).ResultOptionalEnum();
		Failure malformed = new(ParseFailure.Malformed, "x", nameof(Status));
		Result<Status> failed = malformed;

		var result = validator.Validate(new EnumSample(new Success<Status>(Status.Active), failed));

		result.IsValid.ShouldBeFalse();
		result.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe(FailureDetail.Render(malformed));
	}
}
