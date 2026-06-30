using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Norse.Abstractions.Migrations;
using NSubstitute;

namespace Norse.Infrastructure.Migrations.Tests;

public sealed class MigrationRunnerServiceTests
{
	[Fact]
	public async Task StartAsync_runs_all_contributors_and_stops_application()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Test");
		contributor.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var sut = new MigrationRunnerService(
			[contributor],
			lifetime,
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await contributor.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	public async Task StartAsync_with_multiple_contributors_runs_all()
	{
		var a = Substitute.For<IMigrationContributor>();
		a.Name.Returns("A");
		a.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var b = Substitute.For<IMigrationContributor>();
		b.Name.Returns("B");
		b.MigrateAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var sut = new MigrationRunnerService(
			[a, b],
			lifetime,
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StartAsync(CancellationToken.None);

		await a.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
		await b.Received(1).MigrateAsync(Arg.Any<CancellationToken>());
		lifetime.Received(1).StopApplication();
	}

	[Fact]
	public async Task StartAsync_propagates_exception_and_does_not_stop_application()
	{
		var contributor = Substitute.For<IMigrationContributor>();
		contributor.Name.Returns("Bad");
		contributor.MigrateAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new InvalidOperationException("migration failed")));

		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var sut = new MigrationRunnerService(
			[contributor],
			lifetime,
			NullLogger<MigrationRunnerService>.Instance);

		var act = () => sut.StartAsync(CancellationToken.None);

		await act.ShouldThrowAsync<InvalidOperationException>();
		lifetime.DidNotReceive().StopApplication();
	}

	[Fact]
	public async Task StopAsync_is_always_a_noop()
	{
		var sut = new MigrationRunnerService(
			[],
			Substitute.For<IHostApplicationLifetime>(),
			NullLogger<MigrationRunnerService>.Instance);

		await sut.StopAsync(CancellationToken.None);
	}
}
