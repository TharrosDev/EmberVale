"""Spec-author API: dataclasses and helper constructors for quests and dialogues.

Text fields take either prose (a plain `str`: the generator derives the locale key and emits the row)
or `K("existing.key")` (a key already in strings.csv: no row is emitted).
Everything else is data. Nothing here touches the disk.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional, Tuple


class K(str):
    """An existing locale key. Plain `str` values are prose and get a generated key + locale row."""


class OT:
    """ObjectiveType ordinals (src/Quests/QuestTypes.cs; append-only)."""
    KILL, COLLECT, REACH, TALK, ESCORT, DEFEND, INTERACT, STEALTH, MILESTONE = range(9)
    NAMES = ["Kill", "Collect", "Reach", "Talk", "Escort", "Defend", "Interact", "Stealth", "Milestone"]


class C:
    """DialogueCondition ordinals (src/Dialogue/DialogueEnums.cs; append-only)."""
    ALWAYS = 0
    QUEST_AVAILABLE = 1
    QUEST_ACTIVE = 2
    QUEST_COMPLETED = 3
    QUEST_NOT_STARTED = 4
    HAS_FLAG = 5
    MISSING_FLAG = 6
    CORRUPTION_AT_LEAST = 7
    CORRUPTION_BELOW = 8
    COMPANION_RECRUITED = 9
    COMPANION_NOT_RECRUITED = 10
    COMPANION_LOYALTY_AT_LEAST = 11
    SHOP_OPEN = 12
    SHOP_CLOSED = 13
    GUILD_RANK_AT_LEAST = 14
    GUILD_NOT_MEMBER = 15
    GUILD_CAN_JOIN = 16
    REPUTATION_AT_LEAST = 17    # arg 'faction.x:amount'
    COMPANION_IN_PARTY = 18     # arg companion id
    HAS_ITEM = 19               # arg 'item.x:count'


class E:
    """DialogueEffect ordinals (src/Dialogue/DialogueEnums.cs; append-only)."""
    NONE = 0
    START_QUEST = 1
    SET_FLAG = 2
    CLEAR_FLAG = 3
    ADD_CORRUPTION = 4
    RECRUIT_COMPANION = 5
    DISMISS_COMPANION = 6
    ADD_COMPANION_LOYALTY = 7
    LEARN_SPELL = 8
    OPEN_SHOP = 9
    OPEN_SERVICE = 10
    JOIN_GUILD = 11
    GUILD_RANK = 12
    ADD_REPUTATION = 13         # arg 'faction.x:delta'
    GIVE_ITEM = 14              # arg 'item.x:count'
    TAKE_ITEM = 15              # arg 'item.x:count'
    PLAY_CARDS = 16             # arg a story-card key prefix
    TRACK_QUEST = 17            # arg quest id
    BANNER = 18                 # arg chapter or banner key


Cond = Tuple[int, str]     # (C.x, arg)
Eff = Tuple[int, str]      # (E.x, arg)
NO_COND: Cond = (C.ALWAYS, "")
NO_EFF: Eff = (E.NONE, "")


# --- Quests -------------------------------------------------------------------------------------

@dataclass
class Objective:
    type: int
    target: str = ""
    count: int = 1
    text: str = ""              # Description: prose or K(key). Key: <quest id>.obj_<tag>
    tag: str = ""               # stable key tag; defaults to the 1-based position
    location: str = ""          # LocationId (where the player should go)
    req_flag: str = ""
    forbid_flag: str = ""
    completion_flag: str = ""   # set when this objective completes
    activated_flag: str = ""    # set when this objective becomes active
    optional: bool = False
    hint: str = ""              # HintKey: prose or K. Key: <quest id>.hint_<tag>
    journal: str = ""           # JournalEntryKey: prose or K. Key: <quest id>.log_<tag>
    sub_id: str = ""            # .tres sub-resource id; default Obj_<index> (legacy files keep theirs)


def _obj(type_: int, target: str, count: int, text: str, tag: str, kw: dict) -> Objective:
    return Objective(type=type_, target=target, count=count, text=text, tag=tag, **kw)


def kill(target: str, count: int = 1, text: str = "", tag: str = "", **kw) -> Objective:
    return _obj(OT.KILL, target, count, text, tag, kw)


def collect(item: str, count: int = 1, text: str = "", tag: str = "", **kw) -> Objective:
    return _obj(OT.COLLECT, item, count, text, tag, kw)


def reach(target: str, text: str = "", tag: str = "", **kw) -> Objective:
    """Reach: the target IS the destination (so `location=` is refused by --validate)."""
    return _obj(OT.REACH, target, 1, text, tag, kw)


def talk(dialogue: str, text: str = "", tag: str = "", **kw) -> Objective:
    return _obj(OT.TALK, dialogue, 1, text, tag, kw)


def escort(companion: str, destination: str, text: str = "", tag: str = "", **kw) -> Objective:
    kw["location"] = destination
    return _obj(OT.ESCORT, companion, 1, text, tag, kw)


def defend(target: str, seconds: int, text: str = "", tag: str = "", **kw) -> Objective:
    return _obj(OT.DEFEND, target, seconds, text, tag, kw)


def interact(target: str, text: str = "", tag: str = "", count: int = 1, **kw) -> Objective:
    return _obj(OT.INTERACT, target, count, text, tag, kw)


def stealth(target: str, text: str = "", tag: str = "", **kw) -> Objective:
    return _obj(OT.STEALTH, target, 1, text, tag, kw)


def milestone(flag: str, text: str = "", tag: str = "", **kw) -> Objective:
    """Completes when `flag` is set (evaluated on flag change and on unlock)."""
    return _obj(OT.MILESTONE, flag, 1, text, tag, kw)


@dataclass
class Quest:
    id: str
    title: str = ""
    summary: str = ""
    objectives: List[Objective] = field(default_factory=list)
    detail: str = ""              # DetailKey prose (journal): <id>.detail
    xp: int = 250
    gold: int = 75
    gold_item: str = "item.currency.gold"
    reward_items: List[Tuple[str, int]] = field(default_factory=list)
    faction_reward: Optional[Tuple[str, int]] = None
    time_limit: float = 0
    sequential: bool = False
    completion_flag: str = ""
    prerequisite: str = ""
    auto_start: str = ""
    is_main: bool = True
    one_shot: bool = False        # AllowsOneShotTarget: a Kill objective names a one-shot boss
    chapter_key: str = ""         # a locale key (row supplied by the spec's locale_rows or existing)
    order: int = 0
    region: str = ""
    giver_key: str = ""           # a locale key naming the giver (an existing npc name key)
    level: int = 0
    start_flag: str = ""
    fail_flag: str = ""
    ledger: bool = False
    secret: Optional[str] = None  # 'pale' -> keys pale.quest.<tail>.*
    file: str = ""                # override .tres file stem (default derived from the id)


@dataclass
class Patch:
    """Edits an existing hand-authored quest .tres in place: append objectives, set empty fields.
    Never reorders or rewrites existing lines. See tools/campaign/tres.py. Put it in the `quests`
    list a spec's build() returns, next to the Quest objects."""
    quest_id: str
    append: List[Objective] = field(default_factory=list)   # every one needs a tag (key + sub id)
    auto_start: Optional[str] = None
    completion_flag: Optional[str] = None
    start_flag: Optional[str] = None
    fail_flag: Optional[str] = None
    fields: dict = field(default_factory=dict)   # other raw .tres fields, e.g. {"ChapterKey": "chapter.act1"}


# --- Dialogues ----------------------------------------------------------------------------------

@dataclass
class Choice:
    text: str = ""
    goto: str = ""
    cond: Cond = NO_COND
    cond2: Cond = NO_COND
    effect: Eff = NO_EFF
    effect2: Eff = NO_EFF
    tag: str = ""           # key tag: dlg.<name>.<node>.c_<tag>; defaults to the position
    sub_id: str = ""
    compact: bool = False   # hand-authored style: write only non-default Condition/Effect lines
    early: bool = False     # emit this sub-resource in the leading group (file layout only)


@dataclass
class Node:
    id: str
    text: str = ""
    choices: List[Choice] = field(default_factory=list)
    speaker: str = ""       # per-node speaker override (prose or K)
    on_enter: Eff = NO_EFF  # OnEnterEffect
    early: bool = False     # emit this node's sub-resource in the leading group (file layout only)


@dataclass
class StartVariant:
    cond: Cond
    node: str


@dataclass
class Dialogue:
    id: str
    speaker: str = ""
    start: str = "root"
    nodes: List[Node] = field(default_factory=list)
    start_variants: List[StartVariant] = field(default_factory=list)
    secret: Optional[str] = None
    stem: str = ""          # locale key stem override (default: id minus 'dialogue.')
    file: str = ""
    comment: str = ""       # one '; ' comment line above the first sub-resource


def _choice(text, goto, kw) -> Choice:
    when = kw.pop("when", NO_COND)
    when2 = kw.pop("when2", NO_COND)
    do = kw.pop("do", NO_EFF)
    do2 = kw.pop("do2", NO_EFF)
    return Choice(text=text, goto=goto, cond=when, cond2=when2, effect=do, effect2=do2, **kw)


def say(text: str, goto: str = "", **kw) -> Choice:
    """A reply. Optional: when=(C.x, arg), when2=, do=(E.x, arg), do2=, tag=, sub_id=."""
    return _choice(text, goto, kw)


def go(text: str, goto: str, **kw) -> Choice:
    """A reply that navigates to `goto`."""
    return _choice(text, goto, kw)


def leave(text: str, **kw) -> Choice:
    """A reply that ends the conversation."""
    return _choice(text, "", kw)


def set_flag(text: str, flag: str, goto: str = "", **kw) -> Choice:
    kw["do"] = (E.SET_FLAG, flag)
    return _choice(text, goto, kw)


def start_quest(text: str, quest_id: str, goto: str = "", **kw) -> Choice:
    kw["do"] = (E.START_QUEST, quest_id)
    return _choice(text, goto, kw)


def has_flag(flag: str) -> Cond:
    return (C.HAS_FLAG, flag)


def missing_flag(flag: str) -> Cond:
    return (C.MISSING_FLAG, flag)


def corruption_at_least(n: int) -> Cond:
    return (C.CORRUPTION_AT_LEAST, str(n))


def corruption_below(n: int) -> Cond:
    return (C.CORRUPTION_BELOW, str(n))
