"""Worked examples for docs/playbook/campaign-authoring.md. NOT discovered (leading underscore); the
selftest runs it through the whole pipeline in a scratch copy of the data. Ids here are illustrative:
real specs must use ids that exist (see the doc's checklist).
"""

from tools.campaign.model import (
    C, E, K, Dialogue, Node, Quest, StartVariant, collect, corruption_at_least, defend, go, has_flag, interact,
    kill, leave, milestone, missing_flag, reach, say, set_flag, start_quest, talk)

# Example 1: a quest with Talk / Reach / Interact / Defend objectives, an optional objective and a fork.
QUEST = Quest(
    id="quest.main.example_square",
    title="Smoke over the Square",
    summary="Raiders are at the gates. Warn the Elder and hold the square.",
    detail="The Elder will not say it aloud, but he expected this.",
    chapter_key="chapter.act1", order=1, region="region.ember_crown", level=1,
    giver_key="dlg.smith.speaker",
    auto_start="flag.main.opening_done",                  # a code/story-rule flag, or the previous quest's _done
    completion_flag="flag.main.example_square_done",
    objectives=[
        talk("dialogue.elder", "Warn the Elder", tag="warn", hint="He is by the well.",
             journal="The Elder listened without surprise."),
        reach("location.ember_crown.square", "Reach the square", tag="square"),
        interact("interact.alarm_bell", "Ring the alarm bell", tag="bell", completion_flag="flag.beat.bell_rung"),
        defend("location.ember_crown.square", 45, "Hold the square", tag="hold", req_flag="flag.beat.bell_rung",
               hint="Stay inside the ring of torches."),
        collect("item.potion.health", 1, "Bring a draught to the wounded", tag="draught", optional=True),
        milestone("flag.beat.bell_rung", "Sound the alarm", tag="alarm"),
        kill("enemy.goblin", 3, "Drive off the raiders", tag="raiders", forbid_flag="flag.fork.example_parley"),
    ],
    xp=300, gold=100)

# Example 2: a dialogue with start variants, two condition/effect pairs, an OnEnter effect and gates.
DIALOGUE = Dialogue(
    id="dialogue.example_marshal", speaker="Marshal Dray", start="root",
    start_variants=[StartVariant(has_flag("flag.fork.example_parley"), "after")],
    nodes=[
        Node("root", "The Marshal looks you over.", [
            go("Ask about the pass", "pass", tag="pass"),
            set_flag("Spare him", "flag.fork.example_parley", "spared", tag="spare",
                     do2=(E.ADD_REPUTATION, "faction.dawnwardens:10"),
                     when=missing_flag("flag.fork.example_parley")),
            say("Give him to the Syndicate", "sold", tag="sell",
                when=corruption_at_least(40), do=(E.ADD_CORRUPTION, "5"), when2=(C.HAS_ITEM, "item.currency.gold:50"),
                do2=(E.SET_FLAG, "flag.fork.example_sold")),
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

LOCALE = [("chapter.act1", "Act I: Embers at the Crown")]   # rows written by hand (a key, not prose on an object)


def build():
    return [QUEST], [DIALOGUE], LOCALE, "example"
