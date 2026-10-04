"""Shared helpers for the W-Spec-1 modules (Acts I and II). Not discovered by the registry (leading underscore).

`clue()` builds the small placed-interactable dialogues (a cairn, a gauge, a ledger): one node of
description, one closing choice that records a `flag.beat.<slug>_seen` flag, and an `after` node that a
start variant shows once that flag is held. The variant is keyed on the dialogue's OWN flag, never on
the flag its objective sets, so the first interaction cannot race the quest's own completion flag.
"""

from tools.campaign.model import (C, E, Dialogue, Node, StartVariant, has_flag, leave, say)

# Chapter keys (identifiers; their text lives in chapter.<key>.title / .subtitle rows).
CH1, CH2_FROST, CH2_ASHEN, CH2_SUN = "ch.1", "ch.2.frostfang", "ch.2.ashen", "ch.2.sunspire"

CROWN, FROST, ASHEN, SUN = "region.ember_crown", "region.frostfang_reach", "region.ashen_wilds", "region.sunspire"


def seen(slug: str) -> str:
    return f"flag.beat.{slug}_seen"


def clue(slug, speaker, text, close, after, do2=None, do=None, extra=None):
    """A placed interactable. `close` is the player's closing line (it records the seen flag, plus `do2`
    when given). `after` is what the object says on every later visit. `extra` appends more Nodes/choices:
    a list of Node objects, and `do` may replace the seen-flag effect when the effect must be something else."""
    flag = seen(slug)
    first = (E.SET_FLAG, flag) if do is None else do
    second = do2 if do2 is not None else (E.NONE, "")
    nodes = [
        Node("root", text, [leave(close, tag="close", do=first, do2=second)]),
        Node("after", after, [leave("Leave it be.", tag="bye")]),
    ]
    if extra:
        nodes += extra
    return Dialogue(id=f"dialogue.{slug}", speaker=speaker, start="root",
                    start_variants=[StartVariant(has_flag(flag), "after")], nodes=nodes)


def rep(faction: str, delta: int):
    return (E.ADD_REPUTATION, f"{faction}:{delta}")


def give(item: str, n: int):
    return (E.GIVE_ITEM, f"{item}:{n}")


def take(item: str, n: int):
    return (E.TAKE_ITEM, f"{item}:{n}")


def has_item(item: str, n: int):
    return (C.HAS_ITEM, f"{item}:{n}")


def quest_active(qid: str):
    return (C.QUEST_ACTIVE, qid)
