# CLAUDE.md — Midgard (`Norse.Infrastructure`)

## 0. Wrong Root — Halt

Session root must be **Bifröst**, not this repo directly — org-wide settings (`superpowers`, permission rules) only apply from the actual root, and Claude Code never merges a submodule's own `.claude/settings.json` into a parent-launched session. If `claude` was run from inside **Midgard**, stop: don't read further, don't propose changes, don't run anything — tell the user to `cd ../Bifrost` and start there. (This repo's `.claude/settings.json` carries a `SessionStart` hook meant to block this before you ever see this file; if you're reading this anyway, the hook was bypassed, disabled, or failed — halt regardless.)

> **Do not commit, push, or rewrite git history** — stage (`git add`), show the diff, stop; the human reviews and commits. This applies even when a skill's flow includes a commit step. **US English spelling** everywhere — code, comments, docs, commits.

## What This Realm Is

Midgard is **embodied law** — `Norse.Infrastructure`: the concrete implementations of Asgard's contracts. It rides on Asgard, Svartálfheim, Urðarbrunnr, and Naglfar; Yggdrasil and everything above rides on it. The README's chart draws every edge; transitive-first holds throughout.

| Project | Carries | Rule |
|---|---|---|
| `Infrastructure.Web.Server` | The mediator runtime (`Mediator/`: `AddNorsePipeline()`, `Sender`, four behaviors, the three-interceptor gRPC stack), the text channels (`Json/`, `Xml/`, `OpenApi/`), `Validation/` (`ResultRules`), and `DeferredSignIn/` (Asgard's contract, memory-cache implementation) | Interceptor order is law: `UnhandledException` → `PrincipalSeeding` → `OutcomeServer`. STJ/XML machinery never leaves this border (NORSE070) |
| `Infrastructure.Web.Client` | WASM-friendly failure decoding — `OutcomeClientInterceptor` reconstructs `Problem` from the `grpc-status-details-bin` trailer | Stays lean for the browser; no server-only coupling |
| `Infrastructure.Web.Grpc` | The shared wire law — `IdentifierSerializers`: CompatibilityLevel 300 sweep, `Guid` as bare 16-byte RFC 9562 `bytes`, `SequentialGuid`/`DeterministicGuid` scalar serializers | Referenced by both `.Web.Client` and `.Web.Server`; registered by the generated wiring against `RuntimeTypeModel.Default` |
| `gen/Infrastructure.Web.Server.Generator` | `MapNorseGrpcServices()` + server component wiring, and Futhark's XML shape generator under `Xml/` (closure walk, shape law NORSE022–028 plus the closure guards NORSE035–037, `{Contract}XmlShape` emission) | One generator assembly per package — bundled into `Infrastructure.Web.Server`'s `analyzers/dotnet/cs/`, never packed standalone |
| `gen/Infrastructure.Web.Client.Generator` | `AddNorseGrpcClients()` + client component wiring | Bundled into `Infrastructure.Web.Client`'s package the same way |
| `gen/Infrastructure.Web.Grpc.Analyzers` | NORSE080 — no `RuntimeTypeModel` mutation outside `WireModelRegistrationGuard` | Declared once in this realm's `Directory.Analyzers.props` manifest; bundled into `Infrastructure.Web.Grpc`'s package. Doctrine: `../Glitnir/docs/the-runes.md` |
| `gen/Infrastructure.Web.Grpc.Generator.Shared` | Shared source (no `.csproj`): `ContractDiscovery`, `ComponentDiscovery`, `RootNamespaceResolution` | Compiles into both wiring generators; never becomes an assembly |
| `Infrastructure.Backend` | The shared server-side assembly (mirror of `Abstractions.Backend`): `Serialization/` (STJ behind the format-agnostic seam + `MaskedValueJsonConverterFactory`), `Keys/` (`DevelopmentSubjectKeyStore`) | No per-functional-group packages (ruled 2026-08-03). Dev keys rest unwrapped on local disk — never a production path |
| `Infrastructure.Persistence.EntityFramework` | The well-and-wire read law: generic `Repository` closing `IReadRepository<TView>`, promoted-member `PredicateRewriter`, `AddWell<TContext>` with total-mirror validation | Vendor-family named — a document store lands as a sibling project, never here |
| `Infrastructure.Migrations` | `MigrationRunnerService` + `AddNorseMigrationsRunner()` — resolve every `IMigrationContributor`, run, stop the host on success, throw hard on failure | No swallowed exceptions, no partial migration, no silent fallback |
| `Infrastructure.Components.Theme` / `.Theme.FluentUI` | App-wide theme bootstrapping seeded from Naglfar's `Norse.DesignSystem.Tokens` | Theme lives in Midgard; Yggdrasil's hosts consume it (`Hosting.Web.Client`, eventually MAUI) — never the other way |
| `Infrastructure.ServiceDefaults` / `.AspNet` | Aspire's ServiceDefaults convention: OpenTelemetry, health checks, console logging | Ruled into Midgard 2026-06-28 — never Yggdrasil, never Bifröst. Deliberately Norse-free |

## Build & Test

- `dotnet build Midgard.slnx` — warnings are errors; a single warning fails.
- `dotnet test Midgard.slnx` — xUnit v3 + Shouldly on Microsoft.Testing.Platform. **VSTest `--filter` does NOT work** — use `dotnet test tests/<Project> -- --filter-class "*.<ClassName>"`. Four standing skips without Docker (SQL Server Testcontainers in `Infrastructure.Persistence.EntityFramework.Tests`).
- SDK pinned by `global.json`: `11.0.100-` prerelease.
- **ReSharper cleanup guards live in two layers** — `.editorconfig` *and* `Midgard.sln.DotSettings` must agree (the named-argument and `PartialTypeWithSinglePart` settings exist in both because cleanup honored only the DotSettings layer). VS holds the team-shared DotSettings layer in memory: reload the solution before editing that file externally, or VS will clobber the edit on its next save. R# cleanup has severed source-generated `partial` halves before (`KeysJsonContext`, `WellContext`) — the suppression is the guard; don't remove it.

## Architecture Facts (decided — do not re-litigate)

- **The mediator pipeline composes in DI; the gateway machinery is retired** (`../Glitnir/docs/Platform/specs/2026-07-27-mediator-pipeline-retires-gateway-design.md`). `AddNorsePipeline()` registers the four behaviors, `Sender`, and `IPrincipalAccessor` as plain DI citizens — no MediatR, no martinothamar/Mediator, no `InternalsVisibleTo` grants. The dispatch chain is hand-rolled and always was.
- **Enums cross every channel by governed name, and flags ride the contract bare** (2026-08-09 amendment, `../Glitnir/docs/Platform/specs/2026-08-02-futhark-enum-wire-law-design.md`). The channels translate: gRPC keeps the composed varint; JSON renders a governed-name array; XML renders repeated governed-name elements; OpenAPI stamps `type: array` with the governed picklist as `items`. Write decomposes set bits in member-declaration order — composite/aggregate members are never emitted, leftover bits are illegal to write (throw). Read OR-accumulates — unknown token is an accumulable failure with a did-you-mean suggestion, duplicate token is an accumulable `ParseFailure.Duplicate`, empty/absent is the zero value. One `EnumNameTable`, one algorithm, both channels.
- **NORSE029 is deleted, not narrowed** — a `[Flags]` member on a facade contract is law now. The tombstone stays in `Diagnostics.cs`; never renumber the surviving NORSE022–028 or the closure guards NORSE035–037 added since (short-name collision, construction-surface inaccessibility, nested facade controllers).
- **The sign-bit law is zero-extension everywhere.** Runtime `EnumLexical.ToBits` zero-extends 1/2/4-byte underlying types; the generator's `ClosureWalker.ToBits` table and the emitted write-side casts (`WriterEmitter.BitsExpression`) match it exactly, all eight underlying types. Never sign-extend.
- **XML shape discovery is two pipeline nodes.** The syntax node covers the host's own source and carries the incrementality guarantee (`ControllerShapes` tracked step — `IncrementalCachingTests` must keep passing); the reference-closure node walks `compilation.SourceModule.ReferencedAssemblySymbols` (BCL-prefix assemblies skipped) so facade controllers live in their owning realms — Mímir's `CountriesController` was the first. Both merge before the distinct-by-TypeName grouping; the `AddNorseXml` tripwire remains the guard.
- **One generator assembly per shipping package** (fold, 2026-08-09). The XML shape generator is the `Xml/` subfolder of `Infrastructure.Web.Server.Generator` — the standalone `Infrastructure.Web.Server.Xml.Generator` project is gone; never reintroduce it.
- **`ParseFailure.Duplicate` comes from Svartálfheim** — Midgard's flags readers depend on it, so Svartálfheim publishes before Midgard's next standalone/CI build. The XML channel's emitted duplicate wording stays tokenless (`"duplicate value"`, an `XmlReadContext` detail) until the emitters adopt the taxonomy — a recorded divergence, not a bug.
- **The wire model is guarded.** NORSE080 bans `RuntimeTypeModel.Add` outside `WireModelRegistrationGuard`; this realm's `Directory.Build.targets` is byte-identical canonical — the analyzer reaches compilations via the `Directory.Analyzers.props` manifest, both crossings. Read `../Glitnir/docs/the-runes.md` before touching any `Directory.Build.*` or `Directory.Analyzers.props` file.
- **Generator emitters never call `AppendLine` directly** — always `sb.AppendCSharp(...)` with raw string literals; raw-string interiors in emitters and test fixtures are content, never re-indent them.
- **What remains unconverged:** the dashboard-widget half of UI composition. The persistence-foundation plan (`../Glitnir/docs/Midgard/plans/2026-05-21-midgard-persistence-foundation.md`) sits halted at the plan stage awaiting greenlight. Do not scaffold ahead of a converged spec.

## Process

Spec-first, always: brainstorm → spec → plan in `../Glitnir/docs/Midgard/`, human greenlight at each transition. Implementation is subagent-orchestrated and test-driven: every plan's REQUIRED SUB-SKILL line names `superpowers:subagent-driven-development` (the default; `superpowers:executing-plans` is the narrow separate-session fallback) paired with `superpowers:test-driven-development`. Full rule: `../Glitnir/CLAUDE.md` §2.8.

See `../Bifrost/CLAUDE.md` (§2 The Naming Model) and `../Glitnir/CLAUDE.md` (§3 Bounded Context Map) for the full realm table and how Midgard fits the cosmos.
