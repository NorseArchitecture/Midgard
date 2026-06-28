# Midgard

> The realm of humankind, where the will of the gods takes physical form.

![Midgard — the realm of humankind, where the will of the gods descends from Asgard and takes concrete form in the world](https://github.com/user-attachments/assets/fee3325c-7d69-4e78-85a4-328b7fe91f22 "Midgard — where the will of the gods takes physical form")

*Image credit: [@norsemythologyclips](https://www.instagram.com/norsemythologyclips/) — go follow them.*

Embodied law for the Norse Architecture — **`Norse.Infrastructure`**: the concrete implementations of Asgard's contracts. Persistence, messaging, caching, and external integrations live here — the `DbContext` family, EF conventions, repository implementations (including temporal), `JsonControllerBase<TService>`, the mediator runtime, and UI composition. In the dependency chain it rides on Asgard, Svartalfheim, and Urdarbrunnr; Yggdrasil and everything above rides on it.

## Status

This realm is currently a bare shell — no code, no specs converged yet. Design happens first: brainstorm → spec → plan, recorded in Glitnir's `docs/Midgard/`, before any project is scaffolded here.

## The cosmos

Midgard is one realm of the [Norse Architecture](https://github.com/NorseArchitecture). The whole platform composes at [Bifrost](https://github.com/NorseArchitecture/Bifrost) — clone once, cross the bridge, and every session starts there so decisions get brainstormed across the entire landscape, not in isolation. Every design is tried in [Glitnir](https://github.com/NorseArchitecture/Glitnir), the design court, before code is forged here; this realm's specs and plans will live in the court's `docs/Midgard/` once they converge.
