# Embervale UI asset provenance

## Generated raster assets

- `materials/map_ash_vellum.png` — generated with OpenAI ImageGen on 2026-08-30. Text-free neutral cartographic material: smoked vellum over dark iron, warm charcoal/umber palette, no geography or symbols.
- `backgrounds/menu_ashen_causeway.png` — generated with OpenAI ImageGen on 2026-08-30. Text-free 16:9 low-poly dark-fantasy landscape with a ruined causeway and distant ember beacon; composed with negative space for Godot-rendered menu text.
- `emblems/embervale_seal.png` — generated with OpenAI ImageGen on 2026-08-30 after rejecting an over-ornamented first pass. Transparent forged-iron broken-circle and ember motif, used as the title/loading identity mark.

Both assets were generated specifically for Embervale. Player-facing text remains rendered by Godot.

## Hand-authored vector assets

- `icons/controls/*.svg` - drawn by hand for `UiSkin` (2026-10): slider grabber, switch, check and radio marks, dropdown arrow. Same 24 px grid and 1.8 px stroke as `icons/*.svg`; colours are baked in (ember `#d9a340`, bone `#d8cfbf`) because a theme icon cannot be tinted.
- `glyphs/*.svg` - drawn by hand for `UiGlyph` (2026-10): gamepad shapes only (face button, positional face buttons, the four PlayStation symbols, bumper, trigger, d-pad and its four directions, stick, menu and view buttons). A 24-unit grid rasterised at 2x; the button letter is live text set on top by the game, never part of the art.
