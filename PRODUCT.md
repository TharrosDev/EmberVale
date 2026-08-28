# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

Embervale is played by PC and Steam Deck players who explore a seamless open-world fantasy action RPG in either first- or third-person. Their core job is to read the physical world, choose routes, discover locations, fight, loot, and return changed by progression and corruption.

## Product Purpose

Embervale delivers an original, explorable dying fantasy world built around the loop `explore → fight → loot → grow`. Success means the authored world, systemic encounters, quests, travel, economy, and navigation feel like one coherent place rather than disconnected content cells.

## Positioning

Embervale combines classless build breadth, weighty readable combat, and a corruption-reactive world with a resource-driven authored open world whose physical geography must remain authoritative for navigation and gameplay.

## Operating Context

The game runs in Godot 4.7.1 with C#/.NET 8. World content is authored as region resources, streamed cell scenes, deterministic terrain presentation, biome scatter, navigation regions, map-location anchors, quest objective locations, travel nodes, encounters, services, and persistent world-change components. Normal verification uses `dotnet build`, `dotnet test`, content validation, play runs, and the full-cell screenshot harness.

## Capabilities and Constraints

- Preserve the existing region, terrain, streaming, map, quest, travel, save, encounter, and navigation authorities.
- Every reachable shop, service, settlement, dungeon, landmark, quest destination, and POI remains represented through the existing `MapLocationResource` and placed `MapLocationComponent` system.
- Use existing Quaternius-based assets first and keep the established low-poly, grounded-proportion art direction.
- Static collision and navigation carving remain aligned; terrain and cell seams must remain continuous.
- The repository must remain buildable and playable at every commit.
- Inferred from the supplied overhaul brief: all currently implemented POIs and their connecting world space are in scope; no new competing world system is desired.

## Brand Commitments

Preserve the Embervale name, established lore, region identities, restrained environmental storytelling, grounded fantasy scale, and the existing design bible under `docs/DESIGN.md`. Ember Crown should read as a weathered working heartland; Frostfang Reach should read as a harsher, elevated draconic frontier.

## Evidence on Hand

- `docs/LORE.md`, `docs/DESIGN.md`, `docs/ART_STYLE.md`, `docs/ARCHITECTURE.md`, and `docs/WORLD_AUTHORING.md` are authoritative project evidence.
- `data/regions/*.tres`, `scenes/regions/**/*.tscn`, and `data/map_locations/*.tres` contain the implemented world catalogue.
- `tools/world_shots.gd` and `tests/visual_baselines/world_signatures.json` provide render-based world QA.
- No external commercial claims, player research, or published benchmark data is present and none should be fabricated.

## Product Principles

1. Physical geography is the first navigation layer; HUD guidance confirms rather than compensates.
2. Authored locations and systemic wilderness form one continuous world.
3. Every POI has a legible purpose, approach, center, route structure, and memorable landmark.
4. Regional identity comes from composition, terrain, material, vegetation, and silhouette—not asset-count inflation.
5. Gameplay authorities move with the world and are never duplicated.

## Accessibility & Inclusion

Spatial readability must not depend on color alone. Entrances, routes, services, hazards, and landmarks should remain legible through silhouette, lighting value, placement, motion, and redundant map/compass cues.
