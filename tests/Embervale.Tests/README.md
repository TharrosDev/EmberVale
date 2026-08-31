# Embervale.Tests

Headless tests for Embervale's **pure deterministic C# logic**. The authoritative verification
matrix is [`../README.md`](../README.md). Run this layer anywhere with:

```bash
dotnet test Embervale.sln
```

## What lives here

This project is a plain `Microsoft.NET.Sdk` test project (xUnit). It references the
main game assembly but only exercises logic that does **not** touch Godot's native
interop, so it runs under `dotnet test` without the Godot editor:

- `StatTests` — the `Stat` / `StatModifier` ARPG formula and resource classification.
- The files in this directory classify the rule they pin by name; use `dotnet test --list-tests`
  for the current inventory rather than maintaining a stale hand-written list here.

## What does NOT belong here

Anything that constructs a `GodotObject`/`Node`, calls `GD.*`, or relies on
`Godot.Collections.*` needs the engine loaded and is covered by Embervale's existing in-engine
regression and probe harnesses, orchestrated by `world_quality_check.py --mode engine`:

- Combat resolution against a live `StatsComponent` (a `Node`), and `CombatMath`
  crit/mitigation rolls (driven by `GD.Randf`).
- `LootGenerator` / `LootRarity.Roll` (Godot `RandomNumberGenerator`).
- `SaveManager` round-trips (`Godot.Collections.Dictionary` serialization) and each
  component's `Save()`/`Load()` pair.
- `ContentValidator` (reads the content databases, which `GD.Load` `.tres` files).

## Build note

The main `Embervale.csproj` (Godot.NET.Sdk) globs `*.cs` recursively, so the root
project excludes `tests/**` from its own compilation (see the `<Compile Remove>` in
`Embervale.csproj`). Keep test sources under `tests/` so they compile only here.
