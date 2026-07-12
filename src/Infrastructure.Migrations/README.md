# Norse.Infrastructure.Migrations

The `MigrationRunnerService` and `AddNorseMigrationsRunner` extension — the hosted service that calls all `IMigrationContributor` implementations and exits cleanly. Consumed only by the migrations service; never referenced from runtime containers.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
