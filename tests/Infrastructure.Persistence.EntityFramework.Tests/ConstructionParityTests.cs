using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Persistence.EntityFramework.Migrations;
using Norse.Persistence.EntityFramework.PostgreSQL;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// The drift-risk proof the well-composition spec exists to close (§1.3): runtime construction via
// AddNorseWell<TContext> (Task 2, IDbContextFactory-shaped, pooled) and migration-time construction
// via AddNorseMigrationContext<TContext> (Urdarbrunnr, non-pooled, AddDbContext-shaped) must resolve
// to the identical EF model even though they go through two entirely different DI registration
// shapes. WellContext is a test-only synthetic fixture with no real migrations project (see its own
// remarks) — that is not a gap here: comparing Model.FindEntityType(...).GetTableName()/.GetSchema()
// only requires that both DbContextOptions were successfully built, never a live database connection
// or an actual migration.
public sealed class ConstructionParityTests
{
	[Fact]
	async Task AddNorseWell_and_AddNorseMigrationContext_construct_the_identical_model()
	{
		var runtimeBuilder = Host.CreateApplicationBuilder();
		runtimeBuilder.Configuration["ConnectionStrings:test"] = "Host=localhost;Database=test";
		runtimeBuilder.AddNorseWell<WellContext>(NorsePostgresEfProvider.Instance, "test");
		using var runtimeHost = runtimeBuilder.Build();
		await using var runtimeContext = await runtimeHost.Services
			.GetRequiredService<IDbContextFactory<WellContext>>()
			.CreateDbContextAsync(TestContext.Current.CancellationToken);

		var migrationBuilder = Host.CreateApplicationBuilder();
		migrationBuilder.Configuration["ConnectionStrings:test"] = "Host=localhost;Database=test";
		// No real migrations project exists for this synthetic fixture (see WellContext's remarks) —
		// the assembly name is never resolved unless a migration actually runs, only threaded into
		// UseNpgsql's MigrationsAssembly option, so any non-null value is safe for model construction.
		migrationBuilder.AddNorseMigrationContext<WellContext>(NorsePostgresEfProvider.Instance, "test",
			typeof(WellContext).Assembly.GetName().Name!);
		using var migrationHost = migrationBuilder.Build();
		await using var migrationContext = migrationHost.Services.GetRequiredService<WellContext>();

		var runtimeEntity = runtimeContext.Model.FindEntityType(typeof(WidgetEntity))!;
		var migrationEntity = migrationContext.Model.FindEntityType(typeof(WidgetEntity))!;

		runtimeEntity.GetTableName().ShouldBe(migrationEntity.GetTableName());
		runtimeEntity.GetSchema().ShouldBe(migrationEntity.GetSchema());
	}
}
