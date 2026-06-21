# Embervale.Tests

Headless unit tests for Embervale's **pure C# logic**, runnable anywhere with:

```bash
dotnet test Embervale.sln
```

## What lives here

This project is a plain `Microsoft.NET.Sdk` test project (xUnit). It references the
main game assembly but only exercises logic that does **not** touch Godot's native
interop, so it runs under `dotnet test` without the Godot editor:

- `StatTests` — the `Stat` / `StatModifier` ARPG formula and resource classification.
- `LootRarityTests` — the pure `LootRarity.AffixCount` rarity→affix mapping.

## What does NOT belong here (use GUT in-engine)

Anything that constructs a `GodotObject`/`Node`, calls `GD.*`, or relies on
`Godot.Collections.*` needs the engine loaded and must be tested with
[GUT](https://github.com/bitwes/Gut) on a machine with Godot 4.7 .NET installed:

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
