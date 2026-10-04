# Campaign authoring (spec authors)

How to add quests and dialogues to the campaign overhaul. Read `docs/playbook/campaign.md` first (design, ids, beat sheet) and `docs/RECIPES.md` (quest/dialogue shapes). This file is the generator's manual.

You write **spec modules** in Python. `tools/gen_campaign.py` turns them into `data/quests/*.tres`, `data/dialogue/*.tres`, your block of `data/locale/strings.csv` and `data/story/campaign_graph.json`. Never edit a generated `.tres` by hand: the header says so and `--check` fails on drift.

## Workflow

```
python tools/gen_campaign.py            # validate, then write quests, dialogues, locale block, graph (writes nothing on errors)
python tools/gen_campaign.py --check    # regenerate in memory, diff against disk, exit 1 on drift or errors
python tools/gen_campaign.py --graph-only
python tools/gen_campaign.py --list     # every id and the spec that owns it
python tools/campaign/selftest.py       # the generator's own tests
dotnet build Embervale.sln && dotnet test tests/Embervale.Tests     # CampaignGraphTests read the graph
godot --headless --path . -- --validate                              # inside the godot lock; engine-side id checks
```

`python` = `C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`. Run the generator, then `dotnet test`, then `--validate`, in that order. Re-run the generator after every spec edit: the graph test compares the JSON with the files on disk and fails when it is stale.

## Where things live

| Path | What |
| --- | --- |
| `tools/campaign/specs/<name>.py` | **Your spec.** Auto-discovered (not names starting with `_`). One or several per workstream. |
| `tools/campaign/specs/legacy.py` | The four old main quests and the Archivist / Ash Throne dialogues. Edit it to change those. |
| `tools/campaign/specs/_example.py` | The two worked examples below, runnable. Copy from it. |
| `tools/campaign/model.py` | The API (dataclasses, helpers, enums). |
| `tools/campaign/config.py` | Code-set flags and terminal flags (shared by generator and tests). |

A spec module exposes `build()` returning `(items, dialogues, locale_rows, tag)`:

* `items`: a list of `Quest` and `Patch` objects.
* `dialogues`: a list of `Dialogue` objects.
* `locale_rows`: `[(key, text), ...]` for strings you write by hand (chapter titles, banners). Prose on objects is turned into rows for you.
* `tag`: your locale block, `core`, `spec1`, `spec2`, `world` or `ui` (the blocks pre-inserted in `strings.csv`). Only the text between `# --- BEGIN campaign:<tag> ---` and `# --- END campaign:<tag> ---` is ever rewritten. Do not use another workstream's tag.

Optional module attributes: `SECRET = "pale"` (every object in the module is a Pale Concord secret), `CODE_FLAGS = {flag: "who writes it"}` (flags set by code, a scene or a story rule, which no `.tres` writes), `TERMINAL_FLAGS = {flag: "why nothing reads it"}`.

## API cheat sheet

```python
from tools.campaign.model import *
```

**Text.** Any text field takes prose (`"Warn the Elder"`: the generator derives the key and writes the row) or `K("existing.key")` (a key already in `strings.csv`; no row). One physical line, no newlines.

**Objectives.** Every helper takes `text`, `tag=` and keyword options `location, req_flag, forbid_flag, completion_flag, activated_flag, optional, hint, journal, sub_id`.

| Helper | Meaning | Target / count |
| --- | --- | --- |
| `kill(enemy, count=1, text, tag)` | Kill | enemy template id |
| `collect(item, count, text, tag)` | Collect | item id (a granted item does not count) |
| `reach(location, text, tag)` | Reach | the target IS the destination; never pass `location=` |
| `talk(dialogue, text, tag)` | Talk | a dialogue id (`dialogue.x`) |
| `escort(companion, destination, text, tag)` | Escort | companion id; destination required |
| `defend(location, seconds, text, tag)` | Defend | hold a location for N seconds |
| `interact(target, text, tag)` | Interact | interactable id |
| `stealth(target, text, tag)` | Stealth | |
| `milestone(flag, text, tag)` | Milestone | completes when the flag is set |

`OT.KILL..OT.MILESTONE` are the ObjectiveType ordinals 0..8.

**Quest.** `Quest(id, title, summary, objectives, detail=, xp=, gold=, reward_items=[(item, n)], faction_reward=(faction, n), time_limit=, sequential=, completion_flag=, prerequisite=, auto_start=, is_main=True, one_shot=, chapter_key=, order=, region=, giver_key=, level=, start_flag=, fail_flag=, ledger=, secret=)`. `chapter_key` and `giver_key` are locale keys (not prose): reuse an existing key (a speaker key such as `dlg.smith.speaker`) or add the row through `locale_rows`. Set `one_shot=True` when a Kill objective names a one-shot boss, and make sure the offering dialogue gates on the boss being alive.

**Patch (hand-authored quests).** `Patch(quest_id, append=[objectives], auto_start=, completion_flag=, start_flag=, fail_flag=, fields={"ChapterKey": "..."})` goes in `items`. It appends objective sub-resources and fills fields that are empty, nothing else: existing lines and objective order never change, a non-empty field that differs is an error, re-applying is a no-op. Every appended objective needs a `tag` (or a `K` text). Use it for `quest.warband.{bounty,forge,remedies,heart}`. The lines `tools/negative_tests.py` mutates (`CompletionFlagId = "flag.frostfang.passage_open"`, `Description = "quest.warband.heart.obj"`) stay intact. Generated quests (the `legacy` four and yours) are edited in their spec, not patched.

**Dialogue.** `Dialogue(id, speaker, start="root", nodes=[Node(...)], start_variants=[StartVariant(cond, node)])`, `Node(id, text, choices, speaker=, on_enter=(E.x, arg))`. Choice helpers, all taking `tag=`, `when=(C.x, arg)`, `when2=`, `do=(E.x, arg)`, `do2=`:

| Helper | Use |
| --- | --- |
| `say(text, goto="")` | any reply |
| `go(text, goto)` | navigate |
| `leave(text)` | end the conversation |
| `set_flag(text, flag, goto="")` | reply that sets a flag |
| `start_quest(text, quest_id, goto="")` | reply that starts a quest (the effect also tracks it) |

Condition builders: `has_flag(f)`, `missing_flag(f)`, `corruption_at_least(n)`, `corruption_below(n)`; others are `(C.X, arg)` tuples. `C.*` is DialogueCondition 0..19 (`C.REPUTATION_AT_LEAST` arg `faction.x:amount`, `C.COMPANION_IN_PARTY` arg companion id, `C.HAS_ITEM` arg `item.x:count`); `E.*` is DialogueEffect 0..18 (`E.ADD_REPUTATION` `faction.x:delta`, `E.GIVE_ITEM` / `E.TAKE_ITEM` `item.x:count`, `E.PLAY_CARDS` a story-card key prefix, `E.TRACK_QUEST` quest id, `E.BANNER` chapter or banner key). Existing ordinals never change; new ones are appended in `DialogueEnums.cs` and mirrored in `model.py`.

## Worked example 1: a quest

From `specs/_example.py`: Talk, Reach, Interact, Defend, an optional Collect, a Milestone, and a Kill that a fork turns off.

```python
QUEST = Quest(
    id="quest.main.example_square",
    title="Smoke over the Square",
    summary="Raiders are at the gates. Warn the Elder and hold the square.",
    detail="The Elder will not say it aloud, but he expected this.",
    chapter_key="chapter.act1", order=1, region="region.ember_crown", level=1,
    giver_key="dlg.smith.speaker",
    auto_start="flag.main.opening_done",              # a code flag, or the previous quest's _done flag
    completion_flag="flag.main.example_square_done",
    objectives=[
        talk("dialogue.elder", "Warn the Elder", tag="warn", hint="He is by the well.",
             journal="The Elder listened without surprise."),
        reach("location.ember_crown.square", "Reach the square", tag="square"),
        interact("interact.alarm_bell", "Ring the alarm bell", tag="bell", completion_flag="flag.beat.bell_rung"),
        defend("location.ember_crown.square", 45, "Hold the square", tag="hold",
               req_flag="flag.beat.bell_rung", hint="Stay inside the ring of torches."),
        collect("item.potion.health", 1, "Bring a draught to the wounded", tag="draught", optional=True),
        milestone("flag.beat.bell_rung", "Sound the alarm", tag="alarm"),
        kill("enemy.goblin", 3, "Drive off the raiders", tag="raiders", forbid_flag="flag.fork.example_parley"),
    ],
    xp=300, gold=100)
```

Generated keys: `quest.main.example_square.title`, `.summary`, `.detail`, `.obj_warn`, `.hint_warn`, `.log_warn`, `.obj_square`, and so on. The objective that raises `flag.beat.bell_rung` gates the Defend objective and feeds the Milestone. The Kill objective is inert once the fork flag is set (a gated-off objective is skipped, not incomplete). Completing the quest raises `flag.main.example_square_done`, which the next quest names as its `auto_start`.

## Worked example 2: a dialogue

From `specs/_example.py`: a start variant, two condition/effect pairs, an OnEnter effect, a corruption gate and a fork.

```python
DIALOGUE = Dialogue(
    id="dialogue.example_marshal", speaker="Marshal Dray", start="root",
    start_variants=[StartVariant(has_flag("flag.fork.example_parley"), "after")],
    nodes=[
        Node("root", "The Marshal looks you over.", [
            go("Ask about the pass", "pass", tag="pass"),
            set_flag("Spare him", "flag.fork.example_parley", "spared", tag="spare",
                     when=missing_flag("flag.fork.example_parley"),
                     do2=(E.ADD_REPUTATION, "faction.dawnwardens:10")),
            say("Give him to the Syndicate", "sold", tag="sell",
                when=corruption_at_least(40), do=(E.ADD_CORRUPTION, "5"),
                when2=(C.HAS_ITEM, "item.currency.gold:50"), do2=(E.SET_FLAG, "flag.fork.example_sold")),
            start_quest("Take the job", "quest.main.example_square", tag="job",
                        when=(C.QUEST_AVAILABLE, "quest.main.example_square")),
            leave("Leave", tag="bye"),
        ]),
        Node("pass", "The pass is held for now.", [go("Back", "root", tag="back")]),
        Node("spared", "He bows his head. The roads will remember.",
             [leave("Go", tag="go")], on_enter=(E.BANNER, "chapter.act1")),
        Node("sold", "The Syndicate pays on delivery.", [leave("Go", tag="go")]),
        Node("after", "The Marshal nods as you pass.", [leave("Go", tag="go")]),
    ])
```

Generated keys: `dlg.example_marshal.speaker`, `dlg.example_marshal.root`, `dlg.example_marshal.root.c_spare`, and so on. Start variants are tried in order; the first whose condition holds picks the opening node, else `start` is used. A choice carries two (condition, effect) pairs: both conditions must hold to show it, both effects fire when picked. `on_enter` fires when the node is shown. Corruption gates follow the ending's bands: under 40 the clean branch, 40 and over the dark branch, 60 and over only the dark one (`dialogue.ash_throne`).

## Conventions

**Quest ids.** `quest.main.<slug>` for the campaign, slugs from the beat sheet in `campaign.md`. Hand-authored warband quests are patched, never renamed.

**Flags** (all start with `flag.`):

| Pattern | Use |
| --- | --- |
| `flag.main.<slug>_done` | a main quest's `completion_flag`, and the next quest's `auto_start` |
| `flag.fork.<name>` | a fork choice. Absent means the neutral variant. Every fork flag needs a writer and a reader (consequence) |
| `flag.beat.<name>` | a story beat inside a quest (objective `completion_flag`, an `activated_flag`, a gate) |
| `flag.arc.<realm>_ready` | arc gates between quests that can be done in any order |
| `flag.beat.testimony_<who>` | the five Act III testimony flags (name them this way unless campaign.md says otherwise) |

A quest chain is: quest A sets `flag.main.a_done`, quest B has `auto_start="flag.main.a_done"`. Auto-start does not fire on prerequisite completion. A quest whose own completion flag is already held never auto-starts, so legacy saves catch up.

**Locale keys** (derived; override with `K("...")` only to reuse an existing row):

* quests: `quest.main.<slug>.title`, `.summary`, `.detail`, `.obj_<tag>`, `.hint_<tag>`, `.log_<tag>`
* dialogues: `dlg.<name>.speaker`, `dlg.<name>.<node>`, `dlg.<name>.<node>.c_<tag>` where `<name>` is the dialogue id minus `dialogue.`
* A `tag` is the stable part of a key. Without one the 1-based position is used, so reordering untagged objectives renames their keys. Always tag.

**Pale Concord secrecy.** The name appears only under `pale.*` keys (the atlas gate). Mark a quest, a dialogue or a whole module as secret (`secret="pale"` / `SECRET = "pale"`) and its keys become `pale.quest.<id-tail>.*` and `pale.dlg.<name>.*`. The generator and `CampaignGraphTests` fail any non-`pale.` row whose text contains the phrase "Pale Concord" (any case). Do not name it in quest titles, summaries or hints of non-secret quests before the reveal beat.

**Prose.** Follow `docs/LORE.md`: the Sundering is the divine war, the Cataclysm its fallout; the Beast Lord is "he". One physical line per string.

## What to verify before you generate

The generator only warns about ids it cannot find in `data/` (another workstream may be adding them); the engine's `--validate` fails on them. Check each id you use:

```
grep -rn '^Id = "enemy.storm_tyrant"' data/enemies          # enemy templates
grep -rn '^Id = "location.frostfang.arena"' data/map_locations
grep -rn '^Id = "dialogue.elder"' data/dialogue             # dialogue ids (Talk targets, start_quest args)
grep -rn '^Id = "item.potion.health"' data/items
grep -rn 'DefeatFlagId' data/bosses                         # boss flags (code-set, usable as auto_start)
```

Interact targets are authored in cell scenes; confirm the placed interactable exists (`scenes/regions/**`), and that a Reach or Defend location is a placed map location. Then:

* Every `auto_start` / `req_flag` / `forbid_flag` / milestone flag has a writer: another quest, a dialogue, a boss, or a `CODE_FLAGS` entry naming the code that sets it. Anything else fails validation.
* Every completion flag is read (next quest, gate, dialogue condition, scene) or listed in `TERMINAL_FLAGS`.
* A flag read by a quest must be writable before that quest: written by itself, an ancestor in the chain, or a dialogue not owned by a later quest.
* A Collect objective does not count granted items; use Interact or Milestone for gifts.
* A gated Talk objective with nothing else live can finish a quest at once; keep another live objective.
* A dialogue node reachable only through a start variant is fine for the generator, but `DialogueContentTests.EveryNodeIsReachable` only follows `Goto` and `StartNodeId`: link such a node from another node, or extend that test to count `NodeId` of start variants.
* Every generated dialogue node needs at least one choice (use `leave`). Node ids and choice sub-ids are stable: tests and `HeadlessStory` use the legacy ones.

## Generator errors, in plain words

| Message | Fix |
| --- | --- |
| `reads flag.x ... no writer can run before it` | add the writer earlier in the chain, or declare a `CODE_FLAGS` entry |
| `sets flag.x but nothing reads it (chain gap ...)` | connect the next quest, or list the flag in `TERMINAL_FLAGS` |
| `secret leak: 'key' names the Pale Concord outside a pale.* key` | mark the object secret or reword it |
| `duplicate key` | two rows with the same key anywhere in `strings.csv`; rename a tag |
| `locale key 'k' is not in strings.csv` | a `K(...)` or `chapter_key` / `giver_key` with no row; add it to `locale_rows` |
| `node 'n' is unreachable / a dead end` | link it or give it a `leave` |
| `... is hand-authored; a spec may only patch it` | use `Patch`, or pick another id |
| `generated file with no spec behind it` | a spec was removed; delete the stale `.tres` |
| `main quest is not reachable from New Game` | its `auto_start` flag is never raised (see the reachability rule in `tools/campaign/graph.py`) |
