# Midgard

> The realm of humankind, where the will of the gods takes physical form.

<p align="center">
  <img src="https://github.com/user-attachments/assets/fee3325c-7d69-4e78-85a4-328b7fe91f22" alt="Midgard — the realm of humankind, where the will of the gods descends from Asgard and takes concrete form in the world" title="Midgard — where the will of the gods takes physical form" />
</p>

*Image credit: [@norsemythologyclips](https://www.instagram.com/norsemythologyclips/) — go follow them.*

Embodied law for the Norse Architecture — **`Norse.Infrastructure`**: the concrete implementations of Asgard's contracts. Persistence, messaging, caching, and external integrations live here — the `DbContext` family, EF conventions, repository implementations (including temporal), `JsonControllerBase<TService>`, the mediator runtime, and UI composition. In the dependency chain it rides on Asgard, Svartálfheim, and Urðarbrunnr; Yggdrasil and everything above rides on it.

## Status

**`Norse.Infrastructure.Migrations` is live** — `MigrationRunnerService` and `AddNorseMigrationsRunner()` shipped as the runner every Norse migrations service calls through, part of the platform-wide migrations framework proven end to end across six realms (the full story is on [Bifröst's README](https://github.com/NorseArchitecture/Bifrost#readme)). **`Infrastructure.Web.Server`/`.Web.Client` are also live** — the gRPC mediator transport and the `IDeferredSignIn` implementation against Asgard's contract (Himinbjörg is the pending consumer). **`Infrastructure.Web.Grpc` is live** too — the shared wire-law project carrying `IdentifierSerializers` (CompatibilityLevel 300 per contract member, identifiers as bare 16-byte RFC 9562 `bytes` fields, `SequentialGuid`/`DeterministicGuid` custom scalar serializers), referenced by both `Web.Client` and `Web.Server` and registered by the generated client/server wiring — Midgard's one edge to Svartálfheim. **`Infrastructure.Components.Theme`/`.Theme.FluentUI` are live** too — the first slice of UI composition (app-wide theme bootstrapping); the dashboard-widget-composition half remains unconverged, and the `DbContext` family and repository implementations are still unconverged. Design happens first for what's left: brainstorm → spec → plan, recorded in Glitnir's `docs/Midgard/`, before any further project is scaffolded here.

## The cosmos

Midgard is one realm of the [Norse Architecture](https://github.com/NorseArchitecture). The whole platform composes at [Bifröst](https://github.com/NorseArchitecture/Bifrost) — clone once, cross the bridge, and every session starts there so decisions get brainstormed across the entire landscape, not in isolation. Every design is tried in [Glitnir](https://github.com/NorseArchitecture/Glitnir), the design court, before code is forged here; this realm's specs and plans will live in the court's [docs/Midgard/](https://github.com/NorseArchitecture/Glitnir/tree/master/docs/Midgard) once they converge.

## Soundtrack: Guardians of Asgaard
[![Soundtrack: Guardians of Asgaard](https://img.youtube.com/vi/ARnBgW5XgSo/maxresdefault.jpg)](https://www.youtube.com/watch?v=ARnBgW5XgSo)
