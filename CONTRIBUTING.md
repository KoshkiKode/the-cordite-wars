# Contributing to The Cordite Wars: Six Fronts

Thanks for your interest in contributing. This guide covers how to get the project running locally, the coding conventions we follow, and how to submit changes.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Opening the Project](#opening-the-project)
- [Building & Exporting](#building--exporting)
- [Running Tests](#running-tests)
- [Code Style](#code-style)
- [Simulation & Determinism Rules](#simulation--determinism-rules)
- [Branch & Commit Conventions](#branch--commit-conventions)
- [Submitting a Pull Request](#submitting-a-pull-request)
- [Reporting Issues](#reporting-issues)

---

## Prerequisites

- **Godot 4.6** with .NET support (download from [godotengine.org](https://godotengine.org))
- **.NET 9 SDK** ([dotnet.microsoft.com](https://dotnet.microsoft.com/download))
- A C# IDE: **Rider** (recommended) or **Visual Studio Code** with the C# extension

---

## Opening the Project

```bash
git clone https://github.com/KoshkiKode/cordite.git
cd cordite
```

Open Godot, click **Import**, and select the `project.godot` file at the repo root.

The first time you open the project, Godot will build the .NET solution automatically. If it doesn't, go to **Project → Tools → C# → Create C# Solution**.

**Restore NuGet packages:**

```bash
dotnet restore CorditeWars.sln
```

---

## Building & Exporting

Export templates must be installed in Godot before exporting. Go to **Editor → Manage Export Templates** to install the matching version.

Export targets are defined in `export_presets.cfg`. See `RELEASE_INFRASTRUCTURE.md` for the full release pipeline.

| Platform | Notes |
|----------|-------|
| Windows | Requires Windows export template |
| Linux | Requires Linux export template |
| macOS | Requires macOS export template + code signing on macOS host |
| Android | Requires Android export template + keystore |
| iOS | Requires iOS export template + Xcode on macOS |

---

## Running Tests

Unit tests live in `tests/`. Run them from the command line:

```bash
dotnet test CorditeWars.sln
```

Or use the test runner in Rider / VS Code.

Please add tests for any new simulation logic.

---

## Code Style

- **C# conventions** — follow standard .NET naming: `PascalCase` for types and public members, `camelCase` for local variables and parameters, `_camelCase` for private fields.
- **Formatting** — use the `.editorconfig` at the repo root. Rider and VS Code will pick it up automatically.
- **Godot nodes** — scene-specific logic goes in the scene's C# partial class. Shared simulation logic lives in `src/`.
- **No floating-point in simulation** — see the Determinism Rules section below.

---

## Simulation & Determinism Rules

The Cordite Wars uses **deterministic lockstep** networking. Keeping the simulation deterministic is critical — a single non-deterministic value will desync multiplayer sessions.

**Rules:**

1. **No `float` or `double` in simulation code.** Use the fixed-point math types in `src/`. If you need to add a new numeric operation to simulation logic, implement it in fixed-point.
2. **No `System.Random` in simulation code.** Use the deterministic `xoshiro256**` RNG wrapper in `src/`. Seed it from the match seed only.
3. **No `DateTime.Now` or real-time values in simulation.** Simulation time is driven by tick count only.
4. **No dictionary/hash-map iteration in simulation code** unless the iteration order is explicitly guaranteed. Use sorted structures or arrays.
5. **Simulation runs at 30 ticks/sec.** Do not add logic that depends on frame rate inside simulation methods.

If you're unsure whether something is "simulation code" vs "presentation code," ask in the PR description.

---

## Branch & Commit Conventions

**Branch naming:**

```
feat/short-description
fix/short-description
chore/short-description
docs/short-description
perf/short-description
```

**Commit messages** follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add artillery unit with area denial ability
fix: correct resource tick overflow at high unit counts
perf: reduce allocation in pathfinding hot path
chore: update Godot to 4.6.1
docs: expand determinism rules in CONTRIBUTING.md
```

---

## Submitting a Pull Request

1. Fork the repo and create your branch from `main`.
2. Make your changes with tests for any new simulation logic.
3. Run `dotnet test CorditeWars.sln` — all tests must pass.
4. Open a PR against `main` and fill in the PR template.
5. Link the relevant issue in the PR description.

PRs that touch simulation code (pathfinding, combat, resource ticks, RNG, lockstep) require extra review — describe your change thoroughly and note any determinism implications.

---

## Reporting Issues

Use the issue templates:
- **Bug report** — for crashes, desyncs, gameplay bugs
- **Feature request** — for new gameplay ideas or engine improvements

For security vulnerabilities, see [SECURITY.md](./SECURITY.md).
