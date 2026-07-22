using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Migrations.Seeding;

namespace Norse.Infrastructure.Migrations.Tests;

public sealed class SeedRunnerServiceTests
{
	[Fact]
	async Task StartAsync_runs_all_contributors_and_stops_application()
	{
		var contributor = Substitute.For<ISeedContributor>();
		contributor.Name.Returns("Test");
		contributor.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		SeedRunnerService sut = new(
			[contributor],
			lifetime,
			NullLogger<SeedRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await contributor.Received(1).SeedAsync(Arg.Any<CancellationToken>());
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	async Task StartAsync_with_multiple_contributors_runs_all()
	{
		var a = Substitute.For<ISeedContributor>();
		a.Name.Returns("A");
		a.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var b = Substitute.For<ISeedContributor>();
		b.Name.Returns("B");
		b.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		SeedRunnerService sut = new(
			[a, b],
			lifetime,
			NullLogger<SeedRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await a.Received(1).SeedAsync(Arg.Any<CancellationToken>());
		await b.Received(1).SeedAsync(Arg.Any<CancellationToken>());
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	async Task StartAsync_propagates_exception_and_does_not_stop_application()
	{
		var contributor = Substitute.For<ISeedContributor>();
		contributor.Name.Returns("Bad");
		contributor.SeedAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("seed failed")));

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		SeedRunnerService sut = new(
			[contributor],
			lifetime,
			NullLogger<SeedRunnerService>.Instance);

		var act = () => sut.StartAsync(CancellationToken.None);

		await act.ShouldThrowAsync<InvalidOperationException>();
		lifetime.DidNotReceive().StopApplication();
	}

	[Fact]
	async Task StopAsync_is_always_a_noop()
	{
		SeedRunnerService sut = new(
			[],
			Substitute.For<IHostApplicationLifetime>(),
			NullLogger<SeedRunnerService>.Instance);

		await sut.StopAsync(CancellationToken.None);
	}

	[Fact]
	async Task StartAsync_logs_contributor_lifecycle_when_logger_is_enabled()
	{
		var contributor = Substitute.For<ISeedContributor>();
		contributor.Name.Returns("Test");
		contributor.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		SeedRunnerService sut = new([contributor], lifetime, new AlwaysEnabledLogger());
		await sut.StartAsync(CancellationToken.None);

		lifetime.Received(1).StopApplication();
	}

	sealed class AlwaysEnabledLogger : ILogger<SeedRunnerService>
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
	}

	[Fact]
	void AddNorseSeedingRunner_registers_SeedRunnerService_as_hosted_service()
	{
		var services = new ServiceCollection();
		var builder = Substitute.For<IHostApplicationBuilder>();
		builder.Services.Returns(services);

		builder.AddNorseSeedingRunner();

		services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(SeedRunnerService))
			.ShouldBeTrue();
	}
}
