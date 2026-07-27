using Norse.Abstractions.Contracts;
using Norse.Infrastructure.Web.Client.Grpc;

namespace Norse.Infrastructure.Web.Client.Tests.Grpc;

public sealed class OutcomeFactoryTests
{
	[Fact]
	void Creates_a_Failed_outcome_for_a_closed_outcome_type()
	{
		OutcomeFactory<Outcome<BoolResponse>>.CanCreate.ShouldBeTrue();
		Problem problem = new() { Category = ErrorCategory.LockedOut };
		var outcome = OutcomeFactory<Outcome<BoolResponse>>.CreateErr(problem);
		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.ShouldBeSameAs(problem);
	}

	[Fact]
	void Declines_non_outcome_response_types()
	{
		OutcomeFactory<string>.CanCreate.ShouldBeFalse();
	}
}
