using System.Runtime.InteropServices;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Norse.Persistence.EntityFramework;
using Norse.Persistence.EntityFramework.SqlServer;
using Testcontainers.MsSql;

namespace Norse.Infrastructure.Persistence.EntityFramework.Tests;

/// <summary>
///     SQL Server twin of <see cref="PostgresContainerFixture" /> — same synthetic three-widget well
///     against <see cref="WellContext" />, same seed shape (same <see cref="KnownWidgetId" /> semantics:
///     "alpha", CustomerId "C1", EffectiveDate 2026-01-01, Notes "hot", Labels ["featured","new"]).
///     The platform's first Testcontainers use against SQL Server — no real container-fixture
///     precedent exists to copy (see <see cref="DockerAvailability" />'s remarks); this fixture mirrors
///     <see cref="PostgresContainerFixture" />'s shape as closely as the provider difference allows.
///     Image pinned to SQL Server 2025 (not the Testcontainers default 2022 image):
///     <see cref="NorseSqlServerEfProvider" /> forces EF's compatibility-level-170 floor
///     unconditionally, and that floor only applies cleanly against a genuinely 2025+ engine.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
	readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

	/// <summary>
	///     SQL Server's Linux container image is x86_64-only. Discovered live on this task's own arm64
	///     dev host: both the 2022 and 2025 images reproducibly segfault under Docker Desktop's qemu
	///     emulation there (verified via a direct `docker run`, outside this fixture — not a guess). Real
	///     x86_64 CI runners hit neither path. This is a control-flow gate, not a try/catch-and-hope:
	///     checked in <see cref="InitializeAsync" /> before <c>StartAsync</c> is ever called, so an arm64
	///     host never attempts to pull the (multi-gigabyte) image only to watch it die — skip by knowing,
	///     not by downloading first and catching the failure.
	/// </summary>
	public static bool IsSupportedArchitecture { get; } = RuntimeInformation.ProcessArchitecture == Architecture.X64;

	/// <summary>
	///     True once a real, usable container is up. False when Docker was not available at
	///     <see cref="InitializeAsync" /> time, the host architecture is unsupported (see
	///     <see cref="IsSupportedArchitecture" />), or the container process itself failed to come up for
	///     some other reason. Every dependent test checks this (never a null factory) and calls
	///     <c>Assert.Skip</c> itself — a collection fixture's <c>InitializeAsync</c> throwing fails every
	///     test in the collection before any test body runs, so the <c>[Fact(SkipUnless = ...)]</c>
	///     pre-flight gate (Docker-reachable + architecture-supported) alone cannot cover a container that
	///     starts creating but then dies for an unrelated reason.
	/// </summary>
	public bool Available { get; private set; }

	/// <summary>
	///     Set when <see cref="Available" /> is false for a reason beyond "Docker unreachable" — surfaced by every
	///     dependent test's <c>Assert.Skip</c> message.
	/// </summary>
	public string? UnavailableReason { get; private set; }

	// internal, not public — see PostgresContainerFixture's identical remark.
	// null! justified: only ever read from a test gated on Available (equivalently
	// DockerAvailability.IsAvailable) being true, at which point InitializeAsync has already set it.
	internal IDbContextFactory<WellContext> ContextFactory { get; private set; } = null!;

	/// <summary>The seeded "alpha" widget's id — same seed shape as <see cref="PostgresContainerFixture" />.</summary>
	public Guid KnownWidgetId { get; } = Guid.NewGuid();

	/// <summary>
	///     The live container's connection string — Task 6's canary/seek-verification tests need their
	///     own <see cref="DbContextOptionsBuilder{TContext}" /> per test (wired with <c>LogTo</c> into a
	///     test-owned log), so they cannot ride <see cref="ContextFactory" />'s already-fixed options.
	/// </summary>
	public string ConnectionString => _container.GetConnectionString();

	public async ValueTask InitializeAsync()
	{
		if (!DockerAvailability.IsAvailable)
			return;

		if (!IsSupportedArchitecture)
		{
			UnavailableReason =
				$"SQL Server's Linux image is x86_64-only; this host reports {RuntimeInformation.ProcessArchitecture}.";
			return;
		}

		try
		{
			await _container.StartAsync();
		}
		// Two named types, not a bare catch (Exception) — platform law (Glitnir CLAUDE.md §8) is
		// about exception-TYPE breadth, not which line of code the try wraps: narrowing the try block
		// to just this one call reduces blast radius but does not, by itself, make catching
		// Exception safe — a real unrelated bug (a bad connection string, a genuinely dead Docker
		// daemon, disk exhaustion) would still silently degrade to "environment unavailable, skip"
		// instead of failing loudly, which is exactly wrong on real CI, where a skip would mean
		// something is actually broken. ContainerNotRunningException is Testcontainers' own signal
		// that the container process died before becoming ready — the qemu-emulation segfault's
		// direct shape. NotSupportedException is folded in narrowly for one specific, observed cause,
		// not because it's a plausible-sounding BCL type: Testcontainers.MsSql's own
		// MsSqlContainer.FindSqlCmdFilePathAsync() readiness-wait probe throws exactly this type,
		// with message "The sqlcmd binary could not be found," when the container died at a slightly
		// different point during the same qemu-emulated startup — before the probe could even locate
		// the sqlcmd binary inside it — rather than after full readiness-check failure. Confirmed by
		// reproducing both shapes locally, moments apart, same image, same host. Any other exception
		// type is a real bug and propagates.
		catch (Exception ex) when (ex is ContainerNotRunningException or NotSupportedException)
		{
			UnavailableReason = ex.Message;
			return;
		}

		Available = true;

		DbContextOptionsBuilder<WellContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorseSqlServerEfProvider.Instance, _container.GetConnectionString(),
			migrationsAssemblyName: null);
		ContextFactory = new WellContextFactory(optionsBuilder.Options);

		await using var context = await ContextFactory.CreateDbContextAsync();
		// No EfMigrationContributor<WellContext> exists for this synthetic test-only schema — see
		// WellContext.cs's remarks. EnsureCreatedAsync is the pragmatic substitute for a throwaway
		// integration-test model, same deviation PostgresContainerFixture already documents.
		await context.Database.EnsureCreatedAsync();
		await SeedAsync(context);
	}

	async Task SeedAsync(WellContext context)
	{
		var betaId = Guid.NewGuid();
		var gammaId = Guid.NewGuid();
		var alphaEffectiveDate = new DateOnly(2026, 1, 1);
		context.AddRange(
			new WidgetEntity
			{
				Id = KnownWidgetId,
				CustomerId = "C1",
				EffectiveDate = alphaEffectiveDate,
				Tags = [new() { Id = Guid.NewGuid(), WidgetId = KnownWidgetId, Label = "featured" }],
				View = new WidgetView
				{
					Id = KnownWidgetId,
					CustomerId = "C1",
					EffectiveDate = alphaEffectiveDate,
					Name = "alpha",
					Notes = "hot",
					Tags = [new() { Label = "featured" }],
					Labels = ["featured", "new"]
				}
			},
			new WidgetEntity
			{
				Id = betaId,
				CustomerId = "C2",
				EffectiveDate = new DateOnly(2026, 2, 1),
				View = new WidgetView
				{
					Id = betaId,
					CustomerId = "C2",
					EffectiveDate = new DateOnly(2026, 2, 1),
					Name = "beta",
					Notes = "cold"
				}
			},
			new WidgetEntity
			{
				Id = gammaId,
				CustomerId = "C1",
				EffectiveDate = alphaEffectiveDate,
				View = new WidgetView
				{
					Id = gammaId,
					CustomerId = "C1",
					EffectiveDate = alphaEffectiveDate,
					Name = "gamma",
					Notes = "cold",
					Labels = ["legacy"]
				}
			});
		await context.SaveChangesAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (Available)
			await _container.DisposeAsync();
	}

	sealed class WellContextFactory(DbContextOptions<WellContext> options) : IDbContextFactory<WellContext>
	{
		public WellContext CreateDbContext() =>
			new(options);
	}
}
