"""W-Spec-1: locale rows that belong to no single quest or dialogue.

* landing.fork.<name> / epilogue.fork.<name>: the payoff line for each Act I-II fork flag at the Celestial landing and in
  the ending epilogue. W-Spec-2 reads them with K(). <name> is the fork flag minus its 'flag.fork.' prefix.
* bark.<slug>: companion toast lines for data/story/reactions/spec1.json.

The rules in data/story/rules/spec1.json need no text. This module adds no quests or dialogues.
"""

FORK_NAMES = ["dray_spared", "dray_pressed", "succession_hjalvar", "succession_halvar", "herd_slain", "herd_calmed",
              "flock_exposed", "flock_turned", "flock_kin"]

LANDING = {
    "dray_spared":
        "A squad of Dawnwardens holds the first beacon under a Watch banner, and Captain Orsolo Dray is drilling them in a "
        "voice that does not shake. 'The Crossway sends what it can,' he says. 'It sends me too, which is more than I had a "
        "right to expect.'",
    "dray_pressed":
        "The first beacon is held by hired iron and a Syndicate crew who count the cost of everything aloud. A scrap of "
        "black mail hangs at their quartermaster's belt, stamped with a marshal's seal, and nobody meets your eye when you "
        "notice it.",
    "succession_hjalvar":
        "Hjalvar Stormbound has climbed the Stair on a litter carried by his own grandsons, and the clans stand behind him "
        "in a wall, shamans in the middle and the young on the flanks. 'We came as we said we would,' he tells you. "
        "'Slowly. All of us.'",
    "succession_halvar":
        "Halvar One-Hand waits at the second beacon with a dozen of the quick and the unjudged, rope still coiled at their "
        "hips. 'Three hours up and no breakfast,' he says. 'The Syndicate would like its rope back.'",
    "herd_slain":
        "A line of Ash Hunters in scorched leathers holds the third beacon with long bows and no small talk. 'The count "
        "was clean,' their warden says. 'It was the cleanest cull the station ever logged. We would rather it had not "
        "been.'",
    "herd_calmed":
        "A grey elk cow and a dire wolf stand together at the edge of the landing, perfectly calm, with a calf asleep "
        "between them. Maeve Ashby's people hold the beacon beside them, laughing at something the wolf has done.",
    "flock_exposed":
        "Mother Oda's grandson stands at the landing in a faded red robe with a cup of clean water in both hands, coughing "
        "and trying not to. Behind him a handful of former pilgrims carry jars that are empty and that they are proud of.",
    "flock_turned":
        "Tamsin Reed leads two dozen former pilgrims in plain grey up the landing steps, in step, looking at you the way "
        "people look at a lamp. 'They asked to come,' she says. 'I have not yet taught them how to say no to you.'",
    "flock_kin":
        "The deacon's red robe hangs from a post at the landing like a flag nobody raised. The warm air that drifts from "
        "somewhere behind the beacons knows your name, and does not seem surprised to see you.",
}

EPILOGUE = {
    "dray_spared":
        "Orsolo Dray took a captain's chair at the Crossway and kept the road open to everyone, whether they could pay the "
        "toll or not. They say a stranger unlocked his cell, and nobody at the post will say who.",
    "dray_pressed":
        "The Citadel's strongbox paid for a season of barley and a season of silence. The Watch at the Crossway still keeps "
        "a page in its book about a marshal whose pockets were emptied in his own cell.",
    "succession_hjalvar":
        "Hjalvar kept the high seat until the spring thaw and gave it up to a granddaughter who could read the cairns "
        "before she could read letters. The eight clans still walk to the moot stone together.",
    "succession_halvar":
        "Halvar sat the high seat and found it colder than the fire outside the gate. The clans changed their law once, "
        "to let an exile speak at the moot, and he never abused it.",
    "herd_slain":
        "The Wilds quieted, and the Hunters' ledger was the thicker for it. Last Hearth ate well that winter and did not "
        "ask where the meat had come from.",
    "herd_calmed":
        "Last Hearth named the calf. The Hunters wrote one word in the margin of their manual, and by the third winter "
        "other stations were writing it too: smoke.",
    "flock_exposed":
        "The wells at Saffra ran clear, and the Archive's readers came every dawn to measure them. Mother Oda's grandson "
        "learned the gauges and read them aloud each morning to anyone who asked.",
    "flock_turned":
        "Two dozen grey cloaks still fetch water at Saffra Wells and still look up when a stranger passes. Tamsin teaches "
        "them to look at the water instead. Some days it works.",
    "flock_kin":
        "The pilgrim track is empty now. Mother Oda pours a cup into the sand at dusk for a grandson who did not come "
        "home, and the sand has stopped answering.",
}

BARKS = {
    "kael_toren": "Toren's cairn. Somebody has kept the grass pulled for twelve winters, and I never asked who.",
    "wren_square": "You held the line instead of running. In my trade that is half the wage.",
    "tessa_crate": "Stamped twice. Whoever stamps a thing twice is hiding who paid for the first.",
    "kael_dray_spared": "You cut his chains. I did not think you would, and I am glad I was wrong.",
    "kael_dray_pressed": "That was not a question you put to him. That was a hand on his throat. Mind where it goes next.",
    "wren_halvar": "A one-handed guide and a Syndicate rope crew. I have worked worse contracts.",
    "tessa_herd_calmed": "Smoke, not steel. Nobody on the wharf will believe me when I tell it.",
    "kael_herd_slain": "They did not know they were doing wrong. I keep thinking about the calf.",
    "wren_flock_kin": "You told him you hear it. Say it was a lie told on purpose and I will keep walking.",
    "tessa_flock_exposed": "Numbers on a page, and a whole chapel believed them. I should have kept a ledger like that at the hull.",
}


# Locked-brazier prompts for the arc gates (WR-spec1 brazier gates). The old rows still describe the pre-campaign gate.
ARC_LOCKED = {
    "boss.storm_tyrant.arc_locked":
        "The lightning rod stands dead. The clans have not yet named who climbs, and the storm will not answer a stranger.",
    "boss.beast_lord.arc_locked":
        "The brazier will not take. The herds still answer a voice on the plateau, and it is not yours yet.",
    "boss.crimson_prophet.arc_locked":
        "The altar fire will not take. His flock still kneels in the nave, and he is not ready to hear you.",
}


def rows():
    out = list(ARC_LOCKED.items())
    for name in FORK_NAMES:
        out.append((f"landing.fork.{name}", LANDING[name]))
        out.append((f"epilogue.fork.{name}", EPILOGUE[name]))
    for slug, text in BARKS.items():
        out.append((f"bark.{slug}", text))
    return out


def build():
    return [], [], rows(), "spec1"
