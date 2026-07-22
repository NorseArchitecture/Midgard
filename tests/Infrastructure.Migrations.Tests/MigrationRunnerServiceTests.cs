using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Migrations;

namespace Norse.Infrastructure.Migrations.Tests;

public sealed class MigrationRunnerServiceTests
{
	[Fact]
	async Task StartAsync_runs_all_contributors()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Test");
		contributor.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		MigrationRunnerService sut = new(
			[contributor],
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await contributor.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
	}

	[Fact]
	async Task StartAsync_with_multiple_contributors_runs_all()
	{
		var a = Substitute.For<IMigrationContributor>();
		a.Name.Returns("A");
		a.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var b = Substitute.For<IMigrationContributor>();
		b.Name.Returns("B");
		b.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		MigrationRunnerService sut = new(
			[a, b],
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await a.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
		await b.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
	}

	[Fact]
	async Task StartAsync_propagates_exception()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Bad");
		contributor.MigrateAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("migration failed")));

		MigrationRunnerService sut = new(
			[contributor],
			NullLogger<MigrationRunnerService>.Instance);

		var act = () => sut.StartAsync(CancellationToken.None);

		await act.ShouldThrowAsync<InvalidOperationException>();
	}

	[Fact]
	async Task StopAsync_is_always_a_noop()
	{
		MigrationRunnerService sut = new(
			[],
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StopAsync(CancellationToken.None);
	}

	[Fact]
	async Task StartAsync_logs_contributor_lifecycle_when_logger_is_enabled()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Test");
		contributor.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		MigrationRunnerService sut = new([contributor], new AlwaysEnabledLogger());

		await sut.StartAsync(CancellationToken.None);

		await contributor.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
	}

	sealed class AlwaysEnabledLogger : ILogger<MigrationRunnerService>
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
	}

	[Fact]
	void AddNorseMigrationsRunner_registers_MigrationRunnerService_as_hosted_service()
	{
		var services = new ServiceCollection();
		var builder = Substitute.For<IHostApplicationBuilder>();
		builder.Services.Returns(services);

		builder.AddNorseMigrationsRunner();

		services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MigrationRunnerService))
			.ShouldBeTrue();
	}
}
