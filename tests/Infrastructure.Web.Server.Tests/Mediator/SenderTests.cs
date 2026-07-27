using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Contracts;
using Norse.Abstractions.Web.Server.Mediator;
using Norse.Infrastructure.Web.Server.Mediator;

namespace Norse.Infrastructure.Web.Server.Tests.Mediator;

public sealed class SenderTests
{
	[Authorize(Policy = "Test.Open")]
	public sealed record Echo(string Text) : IQueryRequest<string>;

	sealed class EchoHandler : IRequestHandler<Echo, string>
	{
		public ValueTask<Outcome<string>> Handle(Echo request, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Outcome<string>.Ok(request.Text));
	}

	sealed class ThrowingHandler : IRequestHandler<Echo, string>
	{
		public ValueTask<Outcome<string>> Handle(Echo request, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("boom");
	}

	static ServiceProvider Host<THandler>() where THandler : class, IRequestHandler<Echo, string> =>
		new ServiceCollection()
			.AddLogging()
			.AddAuthorizationBuilder().AddPolicy("Test.Open", p => p.RequireAssertion(_ => true)).Services
			.AddNorsePipeline()
			.AddScoped<IRequestHandler<Echo, string>, THandler>()
			.AddSingleton<ISenderDispatch, SenderDispatch<Echo, string>>()
			.BuildServiceProvider();

	[Fact]
	async Task Sends_through_the_full_standard_chain_to_the_handler()
	{
		await using var host = Host<EchoHandler>();
		await using var scope = host.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<PrincipalAccessor>()
			.Seed(new(new System.Security.Claims.ClaimsIdentity(authenticationType: "test")));

		var outcome = await scope.ServiceProvider.GetRequiredService<ISender>()
			.Send(new Echo("hello"), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Norse.Primitives.Success<string> success).ShouldBeTrue();
		success.Value.ShouldBe("hello");
	}

	[Fact]
	async Task A_throwing_handler_degrades_to_a_Fault_outcome_with_a_correlation_id()
	{
		await using var host = Host<ThrowingHandler>();
		await using var scope = host.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<PrincipalAccessor>()
			.Seed(new(new System.Security.Claims.ClaimsIdentity(authenticationType: "test")));

		var outcome = await scope.ServiceProvider.GetRequiredService<ISender>()
			.Send(new Echo("hello"), TestContext.Current.CancellationToken);

		outcome.TryGetValue(out Failed failed).ShouldBeTrue();
		failed.Problem.Category.ShouldBe(ErrorCategory.Fault);
		failed.Problem.CorrelationId.ShouldNotBeNull();
	}

	[Fact]
	async Task An_unmapped_request_type_fails_loudly_naming_the_generated_registration()
	{
		await using var host = new ServiceCollection().AddLogging().AddNorsePipeline().BuildServiceProvider();
		await using var scope = host.CreateAsyncScope();
		var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
			await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Echo("x"), TestContext.Current.CancellationToken));
		exception.Message.ShouldContain("AddNorse");
	}
}
