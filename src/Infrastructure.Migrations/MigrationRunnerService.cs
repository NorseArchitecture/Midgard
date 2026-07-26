using Norse.Abstractions.Migrations;

namespace Norse.Infrastructure.Migrations;

sealed partial class MigrationRunnerService(
	IEnumerable<IMigrationContributor> contributors,
	ILogger<MigrationRunnerService> logger) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken) =>
		Task.WhenAll(contributors.Select(c => RunAsync(c, cancellationToken)));

	public Task StopAsync(CancellationToken cancellationToken) =>
		Task.CompletedTask;

	async Task RunAsync(IMigrationContributor contributor, CancellationToken ct)
	{
		LogStarting(logger, contributor.Name);
		await contributor.MigrateAsync(ct).ConfigureAwait(false);
		LogCompleted(logger, contributor.Name);
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Starting migration contributor {Name}")]
	static partial void LogStarting(ILogger logger, string name);

	[LoggerMessage(Level = LogLevel.Information, Message = "Migration contributor {Name} completed")]
	static partial void LogCompleted(ILogger logger, string name);
}
