# Audio credits

All audio in this directory is **CC0 1.0 Universal (public domain)** — free to use,
no attribution required. Credits are recorded here anyway, as good practice.

## Sources

| Files | Pack | Author | License | Via |
| ----- | ---- | ------ | ------- | --- |
| `sfx/combat/hit.ogg`, `crit.ogg`, `block.ogg`, `sfx/steps/*.ogg` | Impact Sounds | Kenney (kenney.nl) | CC0 1.0 | [Boyquotes/kenney-impact-sounds-for-godot](https://github.com/Boyquotes/kenney-impact-sounds-for-godot) |
| `sfx/combat/swing.ogg`, `sfx/pickup.ogg` | RPG Audio | Kenney (kenney.nl) | CC0 1.0 | [Boyquotes/kenney-rpg-audio-for-godot](https://github.com/Boyquotes/kenney-rpg-audio-for-godot) |
| `ui/click.wav`, `confirm.wav`, `back.wav` | UI Audio | Kenney (kenney.nl) | CC0 1.0 | [Calinou/kenney-ui-audio](https://github.com/Calinou/kenney-ui-audio) |
| `sfx/cast.ogg` | 80 CC0 RPG SFX (`spell_01`) | rubberduck | CC0 1.0 | [OpenGameArt](https://opengameart.org/content/80-cc0-rpg-sfx) |

Original packs: <https://kenney.nl/assets/impact-sounds>, <https://kenney.nl/assets/rpg-audio>,
<https://kenney.nl/assets/ui-audio>.

## How they're used

Files are the swap-in targets for the cue ids in `src/Audio/AudioLibrary.cs`. Each cue loads its
real file when present and falls back to a `ProceduralAudio` placeholder otherwise, so a missing or
future cue is never silent. To replace or add a sound, drop an `.ogg`/`.wav` here and point the cue's
`AudioLibrary` entry at it — no other code changes.
