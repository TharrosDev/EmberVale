"""W-Spec-2, part 3: Act IV, the Celestial assault (missions 26-28 and 30; mission 29 is the legacy
quest.main.celestial edited in legacy.py), the rival beats A8-A10, fork F6 (the Knight's Vigil), the
fork allies at the landing and the epilogue row scheme.

The nine Celestial cells (tools/region_spec_celestial.py): landing (SW), broken_concourse (S), void_edge
(SE) / fallen_choir (W), knight_gate (centre), sundered_stair (E) / shattered_rim (NW), ash_throne (N),
godfall (NE). The assault road runs landing -> concourse -> the Knight's bridge -> gate -> throne; the
choir (west), stair (east) and godfall (north-east of the stair) are short spurs off the gate terrace.
"""

from tools.campaign.model import (
    C, E, K, Dialogue, Node, Quest, StartVariant, corruption_at_least, corruption_below, defend, escort, go,
    has_flag, interact, kill, leave, milestone, missing_flag, reach, say, talk)
from tools.campaign.specs._spec2_common import back, bye, gated_clue, read_clue, t
from tools.campaign.specs.spec1_story import EPILOGUE as S1_EPILOGUE

CODE_FLAGS = {
    "flag.fork.dray_spared": "W-Spec-1 dialogue (F1)", "flag.fork.dray_pressed": "W-Spec-1 dialogue (F1)",
    "flag.fork.succession_hjalvar": "W-Spec-1 dialogue (F2)", "flag.fork.succession_halvar": "W-Spec-1 dialogue (F2)",
    "flag.fork.herd_slain": "W-Spec-1 dialogue (F3)", "flag.fork.herd_calmed": "W-Spec-1 dialogue (F3)",
    "flag.fork.flock_exposed": "W-Spec-1 dialogue (F4)", "flag.fork.flock_turned": "W-Spec-1 dialogue (F4)",
    "flag.fork.flock_kin": "W-Spec-1 dialogue (F4)",
    "flag.beat.choir_passed": "story rules rule.spec2.choir_silenced / rule.spec2.choir_answered (data/story/rules/spec2.json)",
    "flag.beat.anchors_lit": "story rule rule.spec2.anchors_lit (data/story/rules/spec2.json)",
    "flag.beat.stair_unsealed": "story rule rule.spec2.winches_turned (data/story/rules/spec2.json)",
    "flag.party.companion.kael": "StoryRuleDirector party mirror (W-Core-B)",
}

TERMINAL_FLAGS = {
    "flag.main.throne_done": "the last quest's completion; nothing follows the credits (flag.game_complete is the ending's own)",
}

LANDING = "location.celestial.landing"
CHOIR = "location.celestial.fallen_choir"
GODFALL = "location.celestial.godfall"
STAIR = "location.celestial.sundered_stair"
GATE = "location.celestial.knight_gate"
THRONE = "location.celestial.ash_throne"

# --------------------------------------------------------------------------------------------------
# Mission 26: the landing
# --------------------------------------------------------------------------------------------------
CELESTIAL_LANDING = Quest(
    id="quest.main.celestial_landing",
    title="The Landing",
    summary="The library gate lets out on a broken terrace over a void, and the realms you helped have sent what they could spare: an Assembly of banners and bad weather, led by a marshal who has never seen the sky look like this. Light the landing beacon, hold the terrace until the Assembly is through, and learn what the Sixth Flamebearer left at the edge of it.",
    detail="Nothing in the Celestial Realm has been tended since the Sundering. The terrace still holds a whole floor, and the beacon on it still takes a flame. What answers the beacon is not friendly: the dead of heaven come to see who is lighting fires. Hold until the Assembly's banners cross. Then speak to its marshal, and walk to the row of cairns at the terrace's edge.",
    chapter_key="ch.4", order=26, region="region.celestial", level=25,
    giver_key="dlg.assembly_marshal.speaker",
    auto_start="flag.main.truth_done", completion_flag="flag.main.celestial_landing_done",
    sequential=True, xp=1400, gold=500,
    objectives=[
        reach(LANDING, "Step through the library gate onto the Celestial landing", tag="gate",
              hint="The gate is in the library forecourt. It stands open now.",
              journal="The gate let out on a terrace of cracked white stone, under a sky the colour of an old wound."),
        interact("interact.celestial.landing_beacon", "Light the cold beacon on the landing", tag="beacon",
                 location=LANDING, completion_flag="flag.beat.landing_beacon_lit",
                 hint="The cold brazier stands at the terrace's east edge, past the arrival pillars.",
                 journal="The beacon took the flame at once. Whatever was watching from the dark took notice."),
        defend(LANDING, 45, "Hold the landing until the Assembly crosses", tag="hold",
               completion_flag="flag.beat.landing_held",
               hint="Revenants, thralls and zealots will cross the terrace. Fight among the old pillars and keep the beacon alight.",
               journal="The dead of heaven came for the light, and the landing held."),
        talk("dialogue.assembly_marshal", "Speak with the Assembly's marshal", tag="marshal", location=LANDING,
             hint="She stands at the head of the Assembly's lines, near the arrival pillars.",
             journal="The marshal gave me the Assembly's numbers and then stopped, and said that the only thing she knew about the Stair she had learned from the stones behind her."),
        interact("interact.celestial.five_graves", "Read the stones of the five cairns", tag="graves", location=LANDING,
                 completion_flag="flag.beat.graves_read",
                 hint="A row of five cairns stands on the terrace's north edge, and a sixth stone beside them is blank.",
                 journal="Five names, and a sixth stone with nothing on it. Someone kept this row for four hundred years."),
    ])

# --------------------------------------------------------------------------------------------------
# Mission 27: the choir and godfall (gated, not sequential)
# --------------------------------------------------------------------------------------------------
GODFALL_CHOIR = Quest(
    id="quest.main.godfall_choir",
    title="The Choir and Godfall",
    summary="The Knight's bridge stands on stone that was never meant to bear it, and the Assembly cannot cross until the anchors under Godfall are lit. West of the gate the Fallen Choir still sings the last hour of the Sundering to no one. Pass through the Choir, climb to Godfall, and light the three anchors.",
    detail="The Choir was heaven's own singers, and they sing the end of the war over and over. If you carry enough of the fallen's fire, they will take you for kin and let you pass for a small price. If you do not, the echoes must be silenced one by one. Beyond them, where a god's hall came down, three anchors hold the rift's edge together. Light them, and hold the terrace while they catch.",
    chapter_key="ch.4", order=27, region="region.celestial", level=26,
    giver_key="dlg.assembly_marshal.speaker",
    auto_start="flag.main.celestial_landing_done", completion_flag="flag.main.godfall_choir_done",
    xp=1500, gold=550,
    objectives=[
        reach(CHOIR, "Cross the bridge and follow the terrace west to the Fallen Choir", tag="choir",
              completion_flag="flag.beat.choir_reached",
              hint="The Fallen Choir lies on the western terraces, past the Knight's bridge. The way is a short spur off the gate terrace.",
              journal="The Choir sang the last hour of the Sundering to nobody, and the sound was beautiful and wrong."),
        kill("enemy.arcane_echo", 4, "Silence the echoes in the Choir's nave", tag="echoes", location=CHOIR,
             req_flag="flag.beat.choir_reached", forbid_flag="flag.beat.choir_answered",
             completion_flag="flag.beat.choir_silenced",
             hint="If the fallen's fire runs strong in you, the stone singer in the nave may answer instead of striking. Otherwise, break the echoes.",
             journal="The echoes did not scream when they broke. They finished the verse."),
        milestone("flag.beat.choir_passed", "Pass through the Choir", tag="pass", location=CHOIR,
                  req_flag="flag.beat.choir_reached",
                  hint="The way through opens once the echoes are silenced, or once the Choir has answered you.",
                  journal="The Choir let me through, one way or another."),
        reach(GODFALL, "Climb to Godfall, where a god's hall came down", tag="godfall",
              req_flag="flag.beat.choir_passed", completion_flag="flag.beat.godfall_reached",
              hint="Godfall is on the north-eastern terraces, beyond the Sundered Stair.",
              journal="Godfall was a hall's worth of broken heaven, and the three anchors stood in its ruin like candles in a ribcage."),
        interact("interact.celestial.anchor_a", "Light the first anchor", tag="anchor_a", location=GODFALL,
                 req_flag="flag.beat.godfall_reached", completion_flag="flag.beat.anchor_a",
                 hint="The anchors stand in a line up the terrace. Light all three, in any order.",
                 journal="The first anchor remembered it was meant to be holding something up."),
        interact("interact.celestial.anchor_b", "Light the second anchor", tag="anchor_b", location=GODFALL,
                 req_flag="flag.beat.godfall_reached", completion_flag="flag.beat.anchor_b",
                 hint="A thread of white fire runs between the anchors as each one takes the flame.",
                 journal="The second anchor drew a thread of fire to the first, and the thread tightened."),
        interact("interact.celestial.anchor_c", "Light the third anchor", tag="anchor_c", location=GODFALL,
                 req_flag="flag.beat.godfall_reached", completion_flag="flag.beat.anchor_c",
                 hint="The last anchor stands highest, on a spur over the void.",
                 journal="The third anchor closed the line, and far to the south the bridge stopped shuddering."),
        defend(GODFALL, 60, "Hold Godfall while the anchors catch", tag="hold", req_flag="flag.beat.anchors_lit",
               completion_flag="flag.beat.godfall_held",
               hint="The anchors draw the dead. Keep them alight, and fight within the ring of the fallen hall.",
               journal="The anchors held. When the last of the dead had fallen back, the bridge was still."),
    ])

# --------------------------------------------------------------------------------------------------
# Mission 28: the Stair (gated, not sequential)
# --------------------------------------------------------------------------------------------------
SUNDERED_STAIR = Quest(
    id="quest.main.sundered_stair",
    title="The Sundered Stair",
    summary="The Stair the Six climbed lies in pieces across the eastern terraces, and its winches still turn if anyone remembers how. Lower the missing flights, break the sentinels the Stair still obeys, and hold it while it settles. What waits at the top is not the Knight. It is the memory he left there.",
    detail="The Stair was the gods' own road to the throne, and only the last flights are gone. The sentinels were set to turn back anyone who had not been asked. The echo at the top was left by a man who stopped on this Stair once, with one foot raised, and has been trying to take the step ever since.",
    chapter_key="ch.4", order=28, region="region.celestial", level=27,
    giver_key="dlg.assembly_marshal.speaker",
    auto_start="flag.main.godfall_choir_done", completion_flag="flag.main.sundered_stair_done",
    xp=1600, gold=600,
    objectives=[
        reach(STAIR, "Climb down to the Sundered Stair on the eastern terraces", tag="stair",
              completion_flag="flag.beat.stair_reached",
              hint="The Stair is east of the Knight's gate, across the terrace.",
              journal="The Stair hung over the void in pieces, a road that went nowhere any more."),
        interact("interact.celestial.winch_a", "Turn the western winch", tag="winch_a", location=STAIR,
                 req_flag="flag.beat.stair_reached", completion_flag="flag.beat.winch_a",
                 hint="Two winches flank the broken flights. Turn both, in either order.",
                 journal="The winch groaned awake for the first time since the gods died."),
        interact("interact.celestial.winch_b", "Turn the eastern winch", tag="winch_b", location=STAIR,
                 req_flag="flag.beat.stair_reached", completion_flag="flag.beat.winch_b",
                 hint="This one is jammed with ash. Clear it with your hands.",
                 journal="The second winch caught, and the whole Stair began to settle."),
        kill("enemy.stone_sentinel", 3, "Break the stone sentinels the Stair still obeys", tag="sentinels",
             location=STAIR, req_flag="flag.beat.stair_unsealed", completion_flag="flag.beat.sentinels_down",
             hint="They wake when both winches have turned. They are slow and hit hard: stay out of their reach and strike after a swing.",
             journal="The sentinels had been turning travellers back since the gods died. I was the first they could not."),
        defend(STAIR, 45, "Hold the Stair while the last flight lowers", tag="hold", req_flag="flag.beat.sentinels_down",
               completion_flag="flag.beat.stair_held",
               hint="More of the Stair's guardians wake as it settles. Stay near the winches until the last flight meets its landing.",
               journal="The Stair settled, and the last flight met its landing."),
        escort("companion.kael", STAIR, "Let Kael walk the Stair beside you", tag="kael", optional=True,
               req_flag="flag.party.companion.kael",
               hint="Kael wants to climb it himself. Keep pace and keep him alive.",
               journal="Kael climbed the Stair with a hand on the wall and said nothing until the top."),
        talk("dialogue.rival_concourse", "Speak with the Knight's echo at the head of the Stair", tag="echo",
             location=STAIR, req_flag="flag.beat.stair_held", completion_flag="flag.beat.stair_echo",
             hint="It stands where the last flight meets the terrace.",
             journal="The echo was not the Knight, only what he left here: a man who had stopped on a stair and never started again."),
        reach(GATE, "Go to the Knight's Gate", tag="gate", req_flag="flag.beat.stair_echo",
              hint="The gate stands west of the Stair, across the terrace.",
              journal="The road to the gate was open, and the Assembly's banners were already on the bridge."),
    ])

# --------------------------------------------------------------------------------------------------
# Mission 30: the throne
# --------------------------------------------------------------------------------------------------
THRONE_QUEST = Quest(
    id="quest.main.throne",
    title="The Ash Throne",
    summary="Morthul is ash, and the throne is empty for the first time since the gods died. Someone must always sit there. Read what is left of the gods on the dais, if you want to, and then answer it.",
    detail="The throne asks whoever is nearest, and it is patient. What you have become decides which doors it leaves open: a clean hand has one, a hand that has taken everything has the other, and a hand in between may choose. The steles on the dais are the gods' last words about endings. They change nothing, and they may be the only thing worth reading before you answer.",
    chapter_key="ch.4", order=30, region="region.celestial", level=29,
    giver_key="dlg.throne.speaker",
    auto_start="flag.main.celestial_done", completion_flag="flag.main.throne_done",
    xp=3000, gold=1000,
    objectives=[
        interact("interact.celestial.entropy_stele_a", "Read the stele of Light and Order", tag="stele_a", optional=True,
                 location=THRONE, hint="Three steles stand on the dais rim, one for each pair of the dead gods.",
                 journal="Light and Order said they were afraid of the order."),
        interact("interact.celestial.entropy_stele_b", "Read the stele of Life and Wisdom", tag="stele_b", optional=True,
                 location=THRONE, hint="Read them in any order, before or after you speak to the throne.",
                 journal="Life and Wisdom understood too late that an ending is not an injury."),
        interact("interact.celestial.entropy_stele_c", "Read the stele of Strength and Nature", tag="stele_c", optional=True,
                 location=THRONE, hint="The last stele has a line scratched under the gods' own.",
                 journal="Strength and Nature remembered that endings are why the seasons are beautiful."),
        milestone("flag.beat.throne_decided", "Answer the Ash Throne", tag="decide", location=THRONE,
                  hint="Speak to the throne on the dais. What you have become decides which doors it leaves open.",
                  journal="The throne asked, and I answered."),
    ])

# --------------------------------------------------------------------------------------------------
# The landing: beacon, marshal, graves
# --------------------------------------------------------------------------------------------------
BEACON = read_clue(
    "dialogue.celestial_landing_beacon", "The Cold Beacon",
    "The brazier is cold and has been cold since heaven fell. Someone left a faggot of grey wood in it, and a flint, as though they had meant to come back. The flint is warm when you pick it up. The wood takes at the first spark, a pale fire that gives no heat and no smoke, and a long way off, something in the dark turns its head.",
    then="The beacon throws a circle of grey light across the terrace. Beyond the pillars, shapes begin to move toward it.",
    then_button="Strike the flint.", close="Ready your weapon.")


def _marshal() -> Dialogue:
    return Dialogue(
        id="dialogue.assembly_marshal", speaker=t("Marshal Iselle Hearne"), start="root",
        start_variants=[StartVariant(has_flag("flag.main.celestial_landing_done"), "later")],
        nodes=[
            Node("root", t("She is a broad woman in dented plate, with the look of someone who has given a great many orders she did not believe in and has finally been given one she does. <<The Assembly stands behind you, Seventh. Wardens from the Crown, clansmen from the Reach, Hunters from the Wilds, Archive hands and a few of the Prophet's lapsed. Whoever you did right by, you will find among them. Whoever you did wrong, the road remembers that too.>>"), [
                go(t("Who answered the call?"), "allies", tag="allies"),
                go(t("What do you know about this place?"), "place", tag="place"),
                go(t("What happens to the Assembly now?"), "plan", tag="plan"),
                bye("Hold the line, Marshal.", tag="hold")]),
            Node("allies", t("<<Look about you. The pickets by the pillars. The fires along the wall. They came on your word, each for their own reasons, and each of them is carrying something they mean you to take. I would take it. Nobody here is a stranger to what the Stair costs.>>"), [
                back("root", "Something else.", tag="back"), bye("Hold the line, Marshal.", tag="hold")]),
            Node("place", t("<<Nothing but what the gate told us, and the gate tells nothing. A bridge, a gate, a throne. A knight, if the Archive's tales are true, the last of six. The cairns behind me were here when we came. Someone lit a fire in front of them every morning, once. The ashes are very old.>>"), [
                back("root", "Something else.", tag="back"), bye("Hold the line, Marshal.", tag="hold")]),
            Node("plan", t("<<We hold this terrace and anything we can reach from it. You go on. If the bridge falls, we go home. If you fall, we go home. If you reach the throne...>> She does not finish. <<Then it is yours to say what we go home to.>>"), [
                back("root", "Something else.", tag="back"), bye("Hold the line, Marshal.", tag="hold")]),
            Node("later", t("She is at the bridge end, watching the sky as if it might do something. <<We hold the terrace and you hold the road. If you need me, I am the one not looking up.>>"), [
                bye("Hold the line, Marshal.", tag="hold")]),
        ])


MARSHAL = _marshal()


def _graves() -> Dialogue:
    return Dialogue(
        id="dialogue.celestial_graves", speaker=t("The Five Cairns"), start="root", nodes=[
            Node("root", t("Five stones in a row at the terrace's edge, each cut by a hand that was not a mason's. The letters are deep and slightly crooked, as though the carver was working in the dark, or in grief. A sixth stone stands at the end of the row, taller than the rest and blank."), [
                go(t("Read the five names."), "names", tag="names"),
                go(t("Look at the sixth stone."), "sixth", tag="sixth"),
                bye("Step back.", tag="close")]),
            Node("names", t("HADRIK, who carried the first fire and would not set it down. ILSABET, who counted every name and could not stop. SKELD, who called the storm and was answered. ORRIN, who walked into the rot to cure it. CALDUS, who read until a voice answered. Under each name the same line is cut last and cut deepest: THROWN, NOT FALLEN."), [
                go(t("Look at the sixth stone."), "sixth", tag="sixth"),
                bye("Step back.", tag="close")]),
            Node("sixth", t("The sixth stone has no name and no line. It stands as tall as a kneeling man. Someone has worn a hollow in the ground before it, the shape of two knees, over a very long time. There is no seventh stone. You look for one anyway."), [
                go(t("Read the five names."), "names", tag="names"),
                bye("Step back.", tag="close")]),
        ])


GRAVES = _graves()

# --------------------------------------------------------------------------------------------------
# The choir (the corruption-gated route) and the godfall anchors
# --------------------------------------------------------------------------------------------------


def _choir_echo() -> Dialogue:
    return Dialogue(
        id="dialogue.choir_echo", speaker=t("The Choir's Echo"), start="root",
        start_variants=[
            StartVariant(has_flag("flag.beat.choir_answered"), "passed"),
            StartVariant(corruption_below(40), "unknown"),
        ],
        nodes=[
            Node("root", t("One of the echoes does not break pattern when you approach. It turns its face to you instead, a singer of white stone with a mouth full of ash, and it sings your name: not the one you were given. The one the fallen use. <<Kin,>> it sings. <<You have the Six in you. Come. The Choir will let you through, if you sing the last verse with us. It will cost you a little of what you were.>>"), [
                say(t("Sing the last verse."), "sung", tag="sing", do=(E.ADD_CORRUPTION, "5"),
                    do2=(E.SET_FLAG, "flag.beat.choir_answered")),
                bye("No. I will break the echoes instead.", tag="refuse")]),
            Node("sung", t("The Choir takes your voice and gives it back different. The echoes part around you like a congregation around a bell. <<Go on, kin,>> they sing. <<The throne has been waiting for a voice like yours.>>"), [
                bye("Walk on through.", tag="go")]),
            Node("unknown", t("The echoes sing past you. None of them turns. There is nothing in you the Choir recognises, and no part of the song is meant for you. It is a mercy, and it is not."), [
                bye("Then I will break them.", tag="go")]),
            Node("passed", t("The echoes sing on behind you. The road west is open, and the song, at last, is only a song."), [
                bye("Walk on.", tag="go")]),
        ])


CHOIR_ECHO = _choir_echo()

ANCHOR_A = gated_clue(
    "dialogue.celestial_anchor_a", "The First Anchor",
    "The anchor is a standing stone as tall as a man, split down the middle and held together by a collar of bronze that was never made by hands. It is cold. When you lay your palm on the split, the bronze warms, and the stone remembers that it is meant to be holding something up.",
    close="Step back.", gate_flag="flag.beat.godfall_reached",
    dormant="A split standing stone in a collar of bronze that was never made by hands. It is cold and it does not answer. Something is not finished behind you, in the Choir's nave, and the anchor knows it.")
ANCHOR_B = gated_clue(
    "dialogue.celestial_anchor_b", "The Second Anchor",
    "The second anchor is half sunk in the rubble of a hall. A line of white fire runs from it toward the first, thin as a thread, and tightens as you touch it.",
    close="Step back.", gate_flag="flag.beat.godfall_reached",
    dormant="A standing stone half sunk in rubble. It is cold and it does not answer. Something is not finished behind you, in the Choir's nave, and the anchor knows it.")
ANCHOR_C = gated_clue(
    "dialogue.celestial_anchor_c", "The Third Anchor",
    "The third anchor is the highest, on a spur over the void. When it takes the flame, the line of fire runs the whole way down the terrace, and far to the south the bridge stops shuddering.",
    close="Step back.", gate_flag="flag.beat.godfall_reached",
    dormant="The highest of the anchors, on a spur over the void. It is cold and it does not answer. Something is not finished behind you, in the Choir's nave, and the anchor knows it.")

# --------------------------------------------------------------------------------------------------
# The Stair: winches and the Knight's echo (A9)
# --------------------------------------------------------------------------------------------------
WINCH_A = gated_clue(
    "dialogue.celestial_winch_a", "The Western Winch",
    "The winch is a drum of black iron wound with chain as thick as your arm, and the chain runs off into the void. The crank is worn smooth in two places, a hand's width apart, by hands you will not meet. When you set your shoulders to it the drum turns, and far below, stone grinds against stone.",
    close="Step back.", gate_flag="flag.beat.stair_reached",
    dormant="A drum of black iron wound with chain. It will not turn under your hands: the Stair has not yet taken notice of you.")
WINCH_B = gated_clue(
    "dialogue.celestial_winch_b", "The Eastern Winch",
    "The second winch is jammed with ash. You clear it with your hands. When it catches, the first drum answers across the gap, and the whole Stair begins, with a great slow groan, to settle toward its landing.",
    close="Step back.", gate_flag="flag.beat.stair_reached",
    dormant="A second drum, jammed with ash. It will not turn under your hands: the Stair has not yet taken notice of you.")


def _rival_concourse() -> Dialogue:
    return Dialogue(
        id="dialogue.rival_concourse", speaker=t("The Echo of the Ashen Knight"), start="root", nodes=[
            Node("root", t("It is not him. It has his height and his stance and no face at all, a shape of grey ember standing at the head of the last flight, and when it speaks the voice is the Knight's, but younger. <<Seventh. This is where I stopped. Not at the throne: here, with one foot on the last stair, because I knew what was at the top and I could not make my foot move. So I stopped, and part of me has been standing here since. The rest of me went on up.>>"), [
                go(t("What did you know?"), "know", tag="know"),
                go(t("Why guard it, then?"), "why", tag="why"),
                go(t("What do you want from me?"), "want", tag="want"),
                bye("I will be there.", tag="go")]),
            Node("know", t("<<That it asks. That it does not ask for the strong or the wicked. It asks for whoever is nearest, and it is patient. That is why five of us were thrown down: not because we were weak, but because we all said yes too fast.>>"), [
                back("root", "Something else.", tag="back"), bye("I will be there.", tag="go")]),
            Node("why", t("<<Because someone must sit, or nothing ends. Because it must not be anyone who wants to. I have kept it for four hundred years against everyone who wanted to, and I have kept it for you against everyone who did not.>>"), [
                back("root", "Something else.", tag="back"), bye("I will be there.", tag="go")]),
            Node("want", t("<<When you reach the gate I will be there, and I will not be an echo. Choose how you meet me. I would like it to be honest, whichever it is.>>"), [
                back("root", "Something else.", tag="back"), bye("I will be there.", tag="go")]),
        ])


RIVAL_CONCOURSE = _rival_concourse()


# --------------------------------------------------------------------------------------------------
# The gate: the vigil (F6), A10
# --------------------------------------------------------------------------------------------------
def _gate_choices():
    return [
        say(t("Kneel beside him."), "kneel", tag="kneel", do=(E.SET_FLAG, "flag.rival.gate_kneel"),
            do2=(E.SET_FLAG, "flag.beat.gate_vigil_done")),
        say(t("Draw."), "draw", tag="draw", do=(E.SET_FLAG, "flag.rival.gate_draw"),
            do2=(E.SET_FLAG, "flag.beat.gate_vigil_done")),
        say(t("Neither. I came to fight."), "fight", tag="fight", do=(E.SET_FLAG, "flag.beat.gate_vigil_done")),
        leave(t("Not yet."), tag="later"),
    ]


def _rival_gate() -> Dialogue:
    nodes = [
        Node("root", t("He is on one knee in the ring's mouth, the greatsword planted point-down before him, his helm on the ground at his side. He does not look up. <<You came by the Stair,>> he says. <<Then you have met what I left on it. I am not going to ask you to turn back, Seventh. I never could ask that of anyone. There is one door left, and I stand in it. You have a choice about how you cross the ring. You may kneel with me a moment, before. Or you may draw.>>"), _gate_choices()),
        Node("root_trust", t("He is on one knee in the ring's mouth with the greatsword planted before him, and he lifts his head when he hears you, which he did not do before. <<You read it beside me,>> he says. <<I have been thinking about that for the whole of the time you were climbing. I am not going to ask you to turn back, Seventh. There is one door left, and I stand in it. You may kneel with me a moment, before. Or you may draw.>>"), _gate_choices()),
        Node("root_defy", t("He is on one knee in the ring's mouth, and the helm is on, and the voice behind it is level. <<You read it over my objection,>> he says. <<I told you I would not be gentle. I am not going to ask you to turn back. There is one door left, and I stand in it. You may kneel with me a moment, before. Or you may draw.>>"), _gate_choices()),
        Node("kneel", t("You kneel in the ash across from him. Neither of you speaks. It is the longest silence of your life, and the shortest, and in it you hear what he has heard for four hundred years: very far off, something patient, asking. <<Yes,>> he says at last, gently. <<That. You hear it too.>> He puts on the helm. <<Rise, Seventh. Do not kneel again. It is the only thing I ask of you, and I will ask it with a sword.>>"), [
            bye("Rise.", tag="rise", do=(E.GIVE_ITEM, "item.potion.health:2"))], on_enter=(E.ADD_CORRUPTION, "-5")),
        Node("draw", t("The blade comes out of its scabbard in the quiet, like a word nobody else would say first. He lifts his head. There is something in his face that might be relief. <<Good,>> he says, and takes up the greatsword. <<I was afraid you would be kind. Kindness is what the throne would use.>>"), [
            bye("Light the brazier.", tag="light")], on_enter=(E.ADD_CORRUPTION, "3")),
        Node("fight", t("<<Neither,>> he says, and something like a smile goes across the dark of the visor. <<The honest answer. Very well.>> He takes up the greatsword and stands, slowly, like a man getting out of cold water."), [
            bye("Light the brazier.", tag="light")]),
        Node("after", t("He has risen, and the helm is on, and he is waiting beside the brazier with the greatsword in both hands. There is nothing left to say, and neither of you says it."), [
            bye("Light the brazier.", tag="light")]),
    ]
    return Dialogue(
        id="dialogue.rival_gate", speaker=t("The Ashen Knight"), start="root",
        start_variants=[
            StartVariant(has_flag("flag.beat.gate_vigil_done"), "after"),
            StartVariant(has_flag("flag.rival.library_defy"), "root_defy"),
            StartVariant(has_flag("flag.rival.library_trust"), "root_trust"),
        ],
        nodes=nodes)


RIVAL_GATE = _rival_gate()

# --------------------------------------------------------------------------------------------------
# The entropy steles on the dais (optional readings in mission 30)
# --------------------------------------------------------------------------------------------------
STELE_A = read_clue(
    "dialogue.entropy_stele_a", "The Stele of Light and Order",
    "SOLARYN: <<We held it up as long as we could be the ones holding.>> THAROS: <<Everything must end in order. We were afraid of the order.>> Under both, in a rougher hand, one line cut very small: <<Nothing that ends has failed.>>",
    close="Step back.")
STELE_B = read_clue(
    "dialogue.entropy_stele_b", "The Stele of Life and Wisdom",
    "VEYRA: <<I would have healed him if he had let me. He did not need healing. He needed to be allowed to stop.>> NYTH: <<I read it all, and the last page was blank. I understood then. I should have left it blank.>>",
    close="Step back.")
STELE_C = read_clue(
    "dialogue.entropy_stele_c", "The Stele of Strength and Nature",
    "DRAKAR: <<I could have stood against him forever. That was the trouble. I could have stood forever.>> ELYNDRA: <<The seasons end. That is why they are beautiful. We forgot.>> Beneath, scratched with a blade: <<Whoever sits: remember that it is beautiful.>>",
    close="Step back.")


# --------------------------------------------------------------------------------------------------
# The fork allies at the landing: one short conversation each, a payoff line and a once-only gift
# --------------------------------------------------------------------------------------------------
# (dialogue stem, speaker, fork flag, gift item:count, spec1 payoff row or None, gift line)
# The nine Act I-II forks reuse spec1's landing.fork.<name> rows (spec1_story.py: "W-Spec-2 reads them with K()"), so the
# people on the terrace are the ones the player met. The two Pale forks have no spec1 row and speak from landing2.fork.*.
ALLIES = [
    ("dray_spared", "Orsolo Dray", "flag.fork.dray_spared", "item.armor.warden_aegis:1", "dray_spared",
     "He unbuckles the aegis of his own watch and holds it out by the straps. <<It was never mine to keep. Nor is it yours to refuse.>>"),
    ("dray_pressed", "Syndicate Quartermaster", "flag.fork.dray_pressed", "item.potion.health:3", "dray_pressed",
     "A crate is opened without comment. Three draughts, each stamped with a seal that has been scratched out. <<Paid for,>> says the quartermaster. <<Do not ask by whom.>>"),
    ("hjalvar", "Hjalvar Stormbound", "flag.fork.succession_hjalvar", "item.ammo.arrows:60", "succession_hjalvar",
     "His grandsons set down a quiver of clan arrows, sixty fletched for the Stair. <<Count them as you spend them, stranger. We do.>>"),
    ("halvar", "Halvar One-Hand", "flag.fork.succession_halvar", "item.potion.stamina:3", "succession_halvar",
     "He passes you three flasks of something the exiles brew from lichen, bitter and warming. <<For the climb. The Syndicate does not need to know.>>"),
    ("herd_slain", "Hunters' Warden", "flag.fork.herd_slain", "item.ammo.arrows:40", "herd_slain",
     "The warden counts out forty arrows from the station's own stores, as carefully as if they were coin. <<The hunt gives what it can.>>"),
    ("herd_calmed", "Maeve's Herders", "flag.fork.herd_calmed", "item.food.field_ration:4", "herd_calmed",
     "Someone passes you a pack of smoked meat and flat bread, still warm. The wolf watches the hand that gives it, and lets it go."),
    ("flock_exposed", "Mother Oda's Grandson", "flag.fork.flock_exposed", "item.potion.health:3", "flock_exposed",
     "He sets his cup on the stone and digs three draughts out of the bundle at his feet, from the Wells' own clean stores. <<She said you would know what they are for.>>"),
    ("flock_turned", "Tamsin Reed", "flag.fork.flock_turned", "item.material.warding_chalk:3", "flock_turned",
     "She presses three sticks of warding chalk into your palm, plain grey, from the Archive's stores. <<Draw a line they cannot cross, and then tell me which side you stand on.>>"),
    ("flock_kin", "A Voice at the Landing", "flag.fork.flock_kin", "item.material.emberbloom:3", "flock_kin",
     "A bundle lies on the stone beneath the red robe, tied with a cord. Three blooms of emberbloom, warm and unwithered. The voice does not speak again."),
    ("queen_released", "Lamplighter of Vesperhold", "flag.fork.queen_released", "item.food.bread:4", None,
     "He puts a warm loaf into your hands, and another, and two more, with the air of a man who has just learned what bread is for."),
    ("queen_kept", "Runner of Vesperhold", "flag.fork.queen_kept", "item.material.grave_dust:2", None,
     "The girl opens her hand and tips two pinches of grey dust into yours without looking at it. <<It keeps,>> she says again."),
]


def _ally(stem, speaker, fork_flag, gift, spec1_name, gift_line) -> Dialogue:
    boon = f"flag.boon.{stem}"
    root = K(f"landing.fork.{spec1_name}") if spec1_name else K(f"landing2.fork.{stem}")
    return Dialogue(
        id=f"dialogue.ally_{stem}", speaker=t(speaker), start="root", nodes=[
            Node("root", root, [
                say(K("dlg.ally.c_take"), "gift", tag="take", when=missing_flag(boon), do=(E.GIVE_ITEM, gift),
                    do2=(E.SET_FLAG, boon)),
                leave(K("dlg.ally.c_leave"), tag="leave")]),
            Node("gift", K(f"landing2.gift.{stem}"), [leave(K("dlg.ally.c_leave"), tag="leave")]),
        ])


ALLY_DIALOGUES = [_ally(*a) for a in ALLIES]

LANDING2 = {
    "queen_released": "A handful of Vesperhold's people stand at the pillars in coats that do not fit them yet, looking at the sky with the expression of people who have never seen it do anything. One of them carries a lamp that is not lit. <<We aged,>> he says, as if announcing a birthday. <<We have come to see what it was worth. Oswin sent this: bread, hot. He says it will be stale by morning, and that this is the point.>>",
    "queen_kept": "Vesperhold's Steward has sent a runner, a girl of fifteen with a black ribbon at her wrist, standing very straight. <<The Steward says you kept the count and the Queen fell, and the evening ended all at once. She says to tell you it is still evening in the places that matter. She says you will understand.>> The girl does not look away. <<She sends this. It is grave-dust from the Court's own steps. She says it keeps.>>",
}

EPILOGUES = {
    # ending.epilogue.<fork>.<a|b|c>: one card per resolved fork, played after the ending's own epilogue card
    # (EndingSequence.ForkEpilogues). a = the first flag of the fork's row, b = the second, c = the third.
    # The nine Act I-II forks take spec1's epilogue.fork.<name> text (spec1_story.py), so one voice tells each arc.
    "ending.epilogue.queen.a": "The people of Vesperhold aged. Some died that winter and said it was worth it. Oswin's daughter learned to walk, and there is a lamp in the plaza now that he lets go out.",
    "ending.epilogue.queen.b": "Vesperhold's years came due in a single night. The Archive wrote the names of the ones who sat down in the Concord's old registry, and left the last page open for whoever came to count them.",
    "ending.epilogue.vigil.a": "The Knight's greatsword still stands upright before the Ash Throne. No one has moved it. Those who have knelt before it say it feels like being forgiven by something that did not forget.",
    "ending.epilogue.vigil.b": "The songs say you drew first at the gate, and that you never again drew first on anyone who knelt. Nobody has been able to confirm it, and nobody has been willing to test it.",
}
for _slug, _names in (("dray", ("dray_spared", "dray_pressed")), ("succession", ("succession_hjalvar", "succession_halvar")),
                      ("herd", ("herd_slain", "herd_calmed")), ("flock", ("flock_exposed", "flock_turned", "flock_kin"))):
    for _i, _name in enumerate(_names):
        EPILOGUES[f"ending.epilogue.{_slug}.{'abc'[_i]}"] = S1_EPILOGUE[_name]

BARKS = {
    # Companion reactions (data/story/reactions/spec2.json); one toast line each, spoken only by a party member.
    "bark.spec2_kael_quay": "Four hundred years of the same lamp. I would be mad inside a week. How do they stand it?",
    "bark.spec2_wren_registry": "One hand, eleven hundred names, written at dusk. That is not a tyrant. That is someone who could not stop.",
    "bark.spec2_tessa_cradle": "Hull-hands learn early that the sea gives things back late or not at all. This place has the opposite trouble.",
    "bark.spec2_kael_released": "Did you hear the bell? I would ring one like that for my brother, if I knew where he was.",
    "bark.spec2_wren_kept": "Kinder to let them keep it, probably. Do not make me say probably twice.",
    "bark.spec2_tessa_codex": "I liked the line about the gate better than the lines about the gods. A gate I understand.",
    "bark.spec2_kael_graves": "Thrown, not fallen. Somebody dug five graves for people who were not dead yet. That is a man who expected to be wrong.",
    "bark.spec2_wren_stair": "The echo said it stopped here. I know that stair. Everybody has one.",
    "bark.spec2_tessa_vigil": "You knelt. So would I, I think. Do not tell Wren.",
}


LOCALE = [
    ("chapter.ch.4.title", "The Celestial War"),
    ("chapter.ch.4.subtitle", "A stair, a gate, and a chair that must never be empty"),
    ("dlg.ally.c_take", "Accept what they offer."),
    ("dlg.ally.c_leave", "Hold the line."),
    # The brazier at the Knight's gate while the vigil has not been kept (BossSummonComponent.LockedPromptKey).
    ("celestial.ashen_knight.challenge_locked_vigil", "A knight kneels beside the brazier, keeping a vigil. Speak to him before you light it."),
    # The Ash Throne's opening line, remembering fork F6 (dialogue.ash_throne in legacy.py).
    ("dlg.throne.gate_kneel", "The Ash Throne. It is carved from something that was once alive, and it is never empty for long. The stone before its foot is worn into the shape of a kneeling man, and you can feel in your own knees where you knelt beside him."),
    ("dlg.throne.gate_draw", "The Ash Throne. It is carved from something that was once alive, and it is never empty for long. A grey blade lies across the foot of the dais where the Knight let it fall, and you can feel the weight of the one you drew."),
    # Mission 29 (quest.main.celestial, legacy.py).
    ("quest.main.celestial.detail", "The Ashen Knight keeps the one door left to the throne, and keeps it because someone must. He will speak to you before the fight, and what you say to him is remembered. Beyond him, on the highest dais in heaven, Morthul sits in a chair that makes of whoever sits in it a god, and a god, in the end, something that cannot get up."),
    ("quest.main.celestial.hint_knight", "A knight kneels beside the brazier. Speak to him first, then light it. He will rise for the fight whatever you say."),
    ("quest.main.celestial.log_knight", "The Ashen Knight is dead, and the only door to the throne stands open."),
    ("quest.main.celestial.hint_morthul", "Light the brazier at the foot of the dais. Morthul rises once the Knight's vigil is over."),
    ("quest.main.celestial.log_morthul", "Morthul came apart into ash, and the ash did not settle."),
] + [(k, t(v)) for k, v in BARKS.items()] + [(f"landing2.fork.{k}", t(v)) for k, v in LANDING2.items()] \
  + [(f"landing2.gift.{a[0]}", t(a[5])) for a in ALLIES] + [(k, t(v)) for k, v in EPILOGUES.items()]


def build():
    quests = [CELESTIAL_LANDING, GODFALL_CHOIR, SUNDERED_STAIR, THRONE_QUEST]
    dialogues = [BEACON, MARSHAL, GRAVES, CHOIR_ECHO, ANCHOR_A, ANCHOR_B, ANCHOR_C, WINCH_A, WINCH_B,
                 RIVAL_CONCOURSE, RIVAL_GATE, STELE_A, STELE_B, STELE_C] + ALLY_DIALOGUES
    return quests, dialogues, LOCALE, "spec2"
