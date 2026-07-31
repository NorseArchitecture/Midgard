using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

// Synthetic shapes existing purely to drive KeyPredicate's own branches in isolation. Deliberately
// NOT NorseDbContext/INorseEntity-shaped: KeyPredicate.For accepts any plain DbContext, and staying
// off NorseDbContext sidesteps both the mirror-law conventions (irrelevant to key-property
// discovery) and EntityConfigurationApplicationGenerator's single-Tier1-context-per-compilation
// limit (WellContext already claims that slot). Never queried, never connected — KeyPredicate only
// ever touches DbContext.Model, which EF builds without opening a connection, so a placeholder
// Npgsql connection string that's never dialed is enough.
sealed record GuidKeyedEntity
{
	public required Guid Id { get; init; }

	public static void Configure(EntityTypeBuilder<GuidKeyedEntity> builder) =>
		builder.HasKey(e => e.Id);
}

sealed record CustomKeyId(Guid Value);

sealed record CustomKeyedEntity
{
	public required CustomKeyId Id { get; init; }

	public static void Configure(EntityTypeBuilder<CustomKeyedEntity> builder)
	{
		builder.HasKey(e => e.Id);
		builder.Property(e => e.Id).HasConversion(id => id.Value, value => new CustomKeyId(value));
	}
}

sealed record IntKeyedEntity
{
	public required int Id { get; init; }

	public static void Configure(EntityTypeBuilder<IntKeyedEntity> builder) =>
		builder.HasKey(e => e.Id);
}

sealed record CompositeKeyedEntity
{
	public required Guid Id1 { get; init; }
	public required Guid Id2 { get; init; }

	public static void Configure(EntityTypeBuilder<CompositeKeyedEntity> builder) =>
		builder.HasKey(e => new { e.Id1, e.Id2 });
}

sealed record KeylessEntity
{
	public required string Name { get; init; }

	public static void Configure(EntityTypeBuilder<KeylessEntity> builder) =>
		builder.HasNoKey();
}

sealed record ShadowKeyedEntity
{
	public required string Name { get; init; }

	public static void Configure(EntityTypeBuilder<ShadowKeyedEntity> builder)
	{
		builder.Property<Guid>("ShadowId");
		builder.HasKey("ShadowId");
	}
}

sealed class KeyPredicateModelContext(DbContextOptions<KeyPredicateModelContext> options) : DbContext(options)
{
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<GuidKeyedEntity>(GuidKeyedEntity.Configure);
		modelBuilder.Entity<CustomKeyedEntity>(CustomKeyedEntity.Configure);
		modelBuilder.Entity<IntKeyedEntity>(IntKeyedEntity.Configure);
		modelBuilder.Entity<CompositeKeyedEntity>(CompositeKeyedEntity.Configure);
		modelBuilder.Entity<KeylessEntity>(KeylessEntity.Configure);
		modelBuilder.Entity<ShadowKeyedEntity>(ShadowKeyedEntity.Configure);
	}
}

public sealed class KeyPredicateTests
{
	static KeyPredicateModelContext CreateContext()
	{
		DbContextOptionsBuilder<KeyPredicateModelContext> optionsBuilder = new();
		optionsBuilder.UseNpgsql("Host=unused;Database=key_predicate_tests;Username=unused;Password=unused");
		return new KeyPredicateModelContext(optionsBuilder.Options);
	}

	[Fact]
	void A_Guid_key_builds_a_direct_equality_predicate()
	{
		using var context = CreateContext();
		var id = Guid.NewGuid();
		var predicate = KeyPredicate.For<GuidKeyedEntity>(context, id).Compile();
		predicate(new GuidKeyedEntity { Id = id }).ShouldBeTrue();
		predicate(new GuidKeyedEntity { Id = Guid.NewGuid() }).ShouldBeFalse();
	}

	[Fact]
	void A_key_type_with_a_public_Guid_constructor_is_converted_through_that_constructor()
	{
		using var context = CreateContext();
		var id = Guid.NewGuid();
		var predicate = KeyPredicate.For<CustomKeyedEntity>(context, id).Compile();
		predicate(new CustomKeyedEntity { Id = new CustomKeyId(id) }).ShouldBeTrue();
		predicate(new CustomKeyedEntity { Id = new CustomKeyId(Guid.NewGuid()) }).ShouldBeFalse();
	}

	[Fact]
	void A_key_type_that_is_neither_Guid_nor_Guid_constructible_throws_naming_the_key_type()
	{
		using var context = CreateContext();
		var exception = Should.Throw<InvalidOperationException>(() => KeyPredicate.For<IntKeyedEntity>(context, Guid.NewGuid()));
		exception.Message.ShouldContain(typeof(int).ToString());
	}

	[Fact]
	void An_entity_with_no_primary_key_throws_naming_the_entity_type()
	{
		using var context = CreateContext();
		var exception = Should.Throw<InvalidOperationException>(() => KeyPredicate.For<KeylessEntity>(context, Guid.NewGuid()));
		exception.Message.ShouldContain(typeof(KeylessEntity).ToString());
		exception.Message.ShouldContain("no primary key");
	}

	[Fact]
	void An_entity_with_a_composite_primary_key_throws_naming_the_entity_type()
	{
		using var context = CreateContext();
		var exception = Should.Throw<InvalidOperationException>(() => KeyPredicate.For<CompositeKeyedEntity>(context, Guid.NewGuid()));
		exception.Message.ShouldContain(typeof(CompositeKeyedEntity).ToString());
		exception.Message.ShouldContain("composite primary key");
	}

	[Fact]
	void An_entity_with_a_shadow_property_primary_key_throws_naming_the_entity_type()
	{
		using var context = CreateContext();
		var exception = Should.Throw<InvalidOperationException>(() => KeyPredicate.For<ShadowKeyedEntity>(context, Guid.NewGuid()));
		exception.Message.ShouldContain(typeof(ShadowKeyedEntity).ToString());
		exception.Message.ShouldContain("shadow property");
	}
}
