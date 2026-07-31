using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.Abstractions.Backend;
using Norse.Persistence.EntityFramework;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// Mirror-law-conformant synthetic pair, real EF-mapped shapes this time (SyntheticWell.cs's
// PolicyEntity/PolicyView are plain records with no EF configuration at all, sufficient for
// PredicateRewriterTests' pure expression-tree assertions but not for a real query against
// Postgres). CustomerId and EffectiveDate are promoted, composite-indexed scalars — the
// well-and-wire spec §4.2/§9.6 "promoted trio" (design doc calls its illustrative shape "the
// Policy trio": two promoted anchors + one residual leg; SyntheticWell.cs's PolicyEntity/View
// already carries that exact shape but is deliberately unwired for EF, so Task 6 re-homes the
// trio onto WidgetEntity/WidgetView, the pair that actually runs against a real database). Tags
// is a promoted collection retargeting to a real child table; Notes and Name are view-extra
// (residual, JSON-path) scalars with no entity counterpart; Labels is a view-extra (residual)
// JSON-mapped primitive collection — no entity counterpart at all, exercising the
// Unpromoted_json_collection_any_translates_server_side canary (Task 6), distinct from Tags'
// promoted-collection path.
sealed record WidgetTagView
{
	public required string Label { get; init; }
}

sealed record WidgetTagEntity : NorseEntityBase<WidgetTagEntity>, INorseEntity<WidgetTagEntity>
{
	public required Guid Id { get; init; }
	public required Guid WidgetId { get; init; }
	public required string Label { get; init; }

	public static void Configure(EntityTypeBuilder<WidgetTagEntity> builder)
	{
		builder.HasKey(t => t.Id);
		builder.Property(t => t.Label).HasMaxLength(64);
	}
}

sealed record WidgetView
{
	public required Guid Id { get; init; }
	public required string CustomerId { get; init; }
	public required DateOnly EffectiveDate { get; init; }
	public required string Name { get; init; }
	public string? Notes { get; init; }
	public IReadOnlyList<WidgetTagView> Tags { get; init; } = [];
	public IReadOnlyList<string> Labels { get; init; } = [];
}

sealed record WidgetEntity : NorseEntityBase<WidgetEntity>, IViewBearer<WidgetView>, INorseEntity<WidgetEntity>
{
	public required Guid Id { get; init; }
	public required string CustomerId { get; init; }
	public required DateOnly EffectiveDate { get; init; }
	public ICollection<WidgetTagEntity> Tags { get; init; } = [];
	public required WidgetView View { get; init; }

	public static void Configure(EntityTypeBuilder<WidgetEntity> builder)
	{
		builder.HasKey(w => w.Id);
		builder.Property(w => w.CustomerId).HasMaxLength(64);
		// Composite index on the promoted anchor pair (well-and-wire spec §9.6's seek-verification
		// precondition — the §4.2 indexing act is part of the fixture schema, not a migration; see
		// WellContext's own remarks on EnsureCreatedAsync). Leftmost-prefix matching on both Postgres
		// and SQL Server means this single composite index also serves CustomerId-only predicates
		// (RepositorySingleTests' "C1" cases) — the standalone single-column index it replaces bought
		// nothing this one doesn't already cover.
		builder.HasIndex(w => new { w.CustomerId, w.EffectiveDate });
		builder.HasMany(w => w.Tags).WithOne().HasForeignKey(t => t.WidgetId);
		builder.OwnsOne(w => w.View, view =>
		{
			view.ToJson();
			view.OwnsMany(v => v.Tags);
		});
	}
}

// Test-only synthetic schema — no migrations project exists for it (a real migrations chassis for
// a throwaway integration-test model would be scope creep against the well-and-wire slice this
// task proves out); PostgresContainerFixture stands up its schema via EnsureCreatedAsync instead
// of EfMigrationContributor<TContext>.MigrateAsync, a deliberate, documented deviation from every
// other real Norse DbContext's schema-creation path. Declared `partial`: Urðarbrunnr's
// EntityConfigurationApplicationGenerator emits the second partial declaration overriding
// ConfigureNorseEntities once it finds this class (a partial NorseDbContext subclass) alongside
// WidgetEntity/WidgetTagEntity in this project's own compilation.
sealed partial class WellContext(DbContextOptions<WellContext> options) : NorseDbContext(options)
{
	// AddWell<TContext>'s discovery law (Task 5) scans public DbSet<TEntity> properties by CLR
	// reflection, not the built EF model — RepositoryReadTests/PostgresContainerFixture never needed
	// this accessor (they go through context.Set<WidgetEntity>() directly), but AddWellTests does.
	public DbSet<WidgetEntity> Widgets => Set<WidgetEntity>();
}
