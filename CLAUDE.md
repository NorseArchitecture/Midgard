# CLAUDE.md — Midgard (`Norse.Infrastructure`)

## 0. Wrong Root — Halt

If you are reading this because **Midgard itself is the Claude Code session root** — someone ran `claude` from inside this directory instead of `../Bifrost` — stop here. Do not read further, do not propose changes, do not run anything.

Tell the user: every Norse Architecture session starts from **Bifrost**. Org-wide settings (the `superpowers` plugin, permission rules) only apply when Bifrost is the actual session root — Claude Code never merges a submodule's own `.claude/settings.json` into a parent-launched session. Exit, `cd ../Bifrost`, and run `claude` there instead.

This repo's own `.claude/settings.json` carries a `SessionStart` hook that should already have blocked this session before this file was ever read. If you're reading this anyway, hooks were bypassed, disabled, or failed — halt regardless; this rule does not depend on the hook to hold.

---

> **Do not commit, push, or rewrite git history.** Stage edits (`git add`), show the diff, and stop — the human reviews and commits.

> **Use US English spelling** in code, identifiers, comments, docs, and commit/PR copy.

## 1. What This Repository Is

Midgard is **embodied law** — `Norse.Infrastructure`: the concrete implementations of Asgard's contracts. Persistence, messaging, caching, and external integrations live here — the `DbContext` family, EF conventions, repository implementations (including temporal), `JsonControllerBase<TService>`, the mediator runtime, and UI composition. In the dependency chain it rides on Asgard, Svartalfheim, and Urdarbrunnr; Yggdrasil and everything above rides on it.

**`Norse.Infrastructure.Migrations` is live** — `MigrationRunnerService` and `AddNorseMigrationsRunner()` shipped, merged, tagged, and published to NuGet as Task 2 of the cross-realm migrations framework rollout (`../Glitnir/docs/Platform/plans/2026-06-28-migrations-framework-identity-schema.md`). It is the hosted service every Norse migrations service calls through, indirectly, via the source-generated `AddNorseMigrations()` from Urdarbrunnr: resolve every registered `IMigrationContributor`, run them, call `IHostApplicationLifetime.StopApplication()` on success, throw hard on any failure. No swallowed exceptions, no partial migration, no silent fallback.

Everything else in this realm — the `DbContext` family, repository implementations, the mediator runtime — is still a bare shell; no other specs have converged here yet. **`Infrastructure.Components.Theme`/`.Theme.FluentUI` are live** — the first slice of "UI composition": app-wide theme bootstrapping, seeded from Naglfar's generated token package (`../Glitnir/docs/Platform/specs/2026-07-11-blazor-component-architecture-design.md`, Addendum 2026-07-12). The other half of "UI composition" — the mechanism that composes N registered dashboard widgets into a rendered, user-arranged layout — remains unconverged. Before writing any new code outside an already-converged slice (`Infrastructure.Migrations`, `Infrastructure.Components.Theme`/`.Theme.FluentUI`): brainstorm → spec → plan, recorded in `../Glitnir/docs/Midgard/`, per the org's spec-first discipline. Do not scaffold a project structure ahead of a converged spec. A persistence-foundation plan is already filed (`../Glitnir/docs/Midgard/plans/2026-05-21-midgard-persistence-foundation.md`, halted at the plan stage awaiting greenlight) — when it (or any later plan) executes, its REQUIRED SUB-SKILL line names `superpowers:subagent-driven-development` as the default (not a recommendation among equals — `executing-plans` is the narrow fallback for separate-session review checkpoints) paired with `superpowers:test-driven-development` — implementation here is subagent-orchestrated and test-driven, never one without the other (`../Glitnir/CLAUDE.md` §2.8).

See `../Bifrost/CLAUDE.md` (§2 The Naming Model) and `../Glitnir/CLAUDE.md` (§3 Bounded Context Map) for the full realm table and how Midgard fits the rest of the cosmos.
