"""W-Spec-1, Act II arc 2: the Ashen Wilds (missions 13 to 15). See docs/playbook/campaign.md.

last_hearth -> herd_and_hearth (Fork F3, slay or calm the herd) -> beast_lord. Fork flags: flag.fork.herd_slain /
flag.fork.herd_calmed, written by dialogue.ashen_herd_choice. flag.arc.ashen_ready is written by a story rule when
herd_and_hearth is done and read by the Beast Lord brazier.
"""

from tools.campaign.model import (
    C, E, K, Dialogue, Node, Quest, StartVariant, collect, defend, go, has_flag, interact, kill, leave, milestone, reach,
    talk)
from tools.campaign.specs._s1 import ASHEN, CH2_ASHEN, clue, give, has_item, rep, take

CODE_FLAGS = {
    "flag.arc.ashen_ready": "story rule rule.arc_ashen (herd_and_hearth done), read by the Beast Lord brazier scene",
    "flag.beat.herd_decided": "story rules rule.herd_decided_*, data/story/rules/spec1.json",
    "flag.beat.beastend_slain": "story rule rule.beastend_slain (Beast Lord defeated + herd slain), spec1.json",
    "flag.beat.beastend_calmed": "story rule rule.beastend_calmed (Beast Lord defeated + herd calmed), spec1.json",
}

Q13 = Quest(
    id="quest.main.last_hearth",
    title="Last Hearth",
    summary="Eleven survivors keep one fire beside the scar road, and the wolves that used to come to the fence in threes "
            "now come in thirties. Reach Last Hearth, hear Maeve Ashby out, hold the fence when the light drops, and see "
            "what Ada Voss can do with what the ash is doing to her patients.",
    detail="Last Hearth is two roofless houses, a tent line and a fire. The beasts of the scar have always been dangerous, "
           "and lately they have become organised. Maeve will not leave and will not ask anyone to stay. Ada Voss burns "
           "what the ash gets into, and she is running out of things to burn.",
    chapter_key=CH2_ASHEN, order=1, region=ASHEN, level=8, giver_key=K("dlg.ashen_headwoman.speaker"),
    auto_start="flag.main.iron_king_done", completion_flag="flag.main.last_hearth_done",
    sequential=True, xp=250, gold=80, reward_items=[("item.potion.health", 2)],
    objectives=[
        reach("location.ashen.last_hearth", "Reach Last Hearth in the Ashen Wilds", tag="hearth",
              hint="Go through the Ashen Breach east of the capital, then follow the scar road to the one lit fire.",
              journal="Last Hearth is a fire, two roofless houses, and eleven people who stopped expecting company."),
        talk("dialogue.ashen_headwoman", "Speak with Maeve Ashby", tag="maeve", location="location.ashen.last_hearth",
             completion_flag="flag.beat.hearth_raid",
             hint="Maeve sits by the fence, ten paces nearer the dark than anyone else.",
             journal="Maeve said the wolves have started to come in thirties, standing in a ring, waiting."),
        defend("location.ashen.last_hearth", 60, "Hold the fence at Last Hearth", tag="fence",
               req_flag="flag.beat.hearth_raid", completion_flag="flag.beat.hearth_held",
               hint="Keep your back to the fire and fight at the fence line. They come in a ring, not a line.",
               journal="The wolves broke at the fence and went back into the scar all at once, as if something had called them off."),
        collect("item.material.healing_herb", 3, "Gather three healing herbs for Ada Voss", tag="herbs", optional=True,
                location="location.ashen.last_hearth",
                hint="Healing herb grows in the green patches at the edge of the scar. Optional: Ada can make a calming smoke from it.",
                journal="You brought Ada herbs for a calming smoke."),
        talk("dialogue.ashen_mender", "See to Ada Voss's wounded", tag="ada", location="location.ashen.last_hearth",
             req_flag="flag.beat.hearth_held",
             hint="Ada works in the tent line by the second house.",
             journal="Ada said the ash is calling both the wolves and her patients, and sent you to the Hunters' station."),
    ])

Q14 = Quest(
    id="quest.main.herd_and_hearth",
    title="Herd and Hearth",
    summary="Something on the plateau is calling the beasts of the scar, and Last Hearth is in the way. Climb to the Ash "
            "Hunters' station, read three tracks yourself, find where the herds meet, and decide what to do with them.",
    detail="Hask Morrow counts packs the way other men count coin, and his numbers have stopped adding up: wolf and elk and "
           "boar walking the same road in step, at night, towards the plateau. Predator and prey do not do that. The three "
           "tracks he has staked are all he has. Where they meet is a herd the Hunters cannot name and a choice nobody "
           "else wants to make.",
    chapter_key=CH2_ASHEN, order=2, region=ASHEN, level=9, giver_key=K("dlg.ash_hunter_warden.speaker"),
    auto_start="flag.main.last_hearth_done", completion_flag="flag.main.herd_and_hearth_done",
    sequential=True, xp=300, gold=100, reward_items=[("item.potion.health", 2)],
    objectives=[
        reach("location.ashen.station", "Climb to the Ash Hunters' station on Hunters' Rise", tag="rise",
              hint="Follow the scar road north and east from Last Hearth, up the rise that watches the plateau road.",
              journal="The Hunters' station looks down the whole scar road, and the road is full of ash."),
        talk("dialogue.ash_hunter_warden", "Hear Hask Morrow's count", tag="hask", location="location.ashen.station",
             completion_flag="flag.beat.hask_briefed",
             hint="Hask is at the rail of the rise with his hand over his eyes.",
             journal="Hask has staked three tracks. He says the herds are walking in step."),
        interact("interact.ashen.beast_track_a", "Read the first beast track", tag="track_a",
                 location="location.ashen.station", completion_flag="flag.beat.track_a_read",
                 hint="Hask's first red stake is at the foot of the rise beside the scar road.",
                 journal="Elk and wolf, walking together. Neither ran."),
        interact("interact.ashen.beast_track_b", "Read the second beast track", tag="track_b",
                 location="location.ashen.station", completion_flag="flag.beat.track_b_read",
                 hint="The second stake is a little higher on the rise, where the road bends towards the wall.",
                 journal="The maw tracks keep station beside the elk. Every print points towards the wall."),
        interact("interact.ashen.beast_track_c", "Read the third beast track", tag="track_c",
                 location="location.ashen.herd_trail", completion_flag="flag.beat.tracks_read",
                 hint="The third stake is where the road meets the plateau ramp. Hask says the trail starts there.",
                 journal="A road trampled hand-deep, and a ring of stones set around a single antler."),
        reach("location.ashen.herd_trail", "Follow the tracks to where the herds gather", tag="trail",
              hint="The three tracks join at the foot of the plateau ramp. Walk the trampled road to its end.",
              journal="Forty animals of six kinds, standing shoulder to shoulder in the ash, waiting."),
        talk("dialogue.ashen_herd_choice", "Decide what to do with the herd", tag="herd",
             location="location.ashen.herd_trail",
             hint="Cull the herd, or break the call with calming smoke. The smoke needs three healing herbs.",
             journal="You stood in front of them and picked."),
        milestone("flag.beat.herd_decided", "Settle the herd's fate", tag="decide", location="location.ashen.herd_trail",
                  hint="Speak to the herd again if you walked away. Cull it, or burn three healing herbs for the smoke.",
                  journal="The herd has your answer."),
        kill("enemy.dire_wolf", 4, "Cut down the herd's lead wolves", tag="cull", location="location.ashen.herd_trail",
             req_flag="flag.fork.herd_slain",
             hint="The wolves come first and the rest follow. Fight on the ramp road, not in the open ash.",
             journal="The lead wolves are dead and the herd with them."),
        defend("location.ashen.herd_trail", 30, "Keep the smoke burning while the herd settles", tag="smoke",
               req_flag="flag.fork.herd_calmed", completion_flag="flag.beat.herd_calm_held",
               hint="Stay upwind of the coals and keep the herbs burning. The wolves will test you once.",
               journal="The smoke held. The herd forgot what it had been called to do."),
    ])

Q15 = Quest(
    id="quest.main.beast_lord",
    title="The Fourth Flamebearer",
    summary="The herds are quiet or gone, and the plateau road is open. The Beast Lord's brazier is cold and will take your "
            "flame. When he falls, tell Last Hearth the wolves will stop coming.",
    detail="He was a Flamebearer who walked into the Wilds alone to cure the corruption at its root, and he did not walk "
           "out. Hask says the rot offered him strength that never tires and senses that never sleep, and he accepted a "
           "little at a time. The beasts go to him. Whatever is left of the man still answers to his name.",
    chapter_key=CH2_ASHEN, order=3, region=ASHEN, level=10, giver_key=K("dlg.ashen_headwoman.speaker"),
    auto_start="flag.main.herd_and_hearth_done", completion_flag="flag.main.beast_lord_done",
    one_shot=True, sequential=True, xp=500, gold=160, reward_items=[("item.potion.health", 2)],
    objectives=[
        talk("dialogue.ash_hunter_warden", "Ask Hask about the lights on the plateau road", tag="hearsay", optional=True,
             location="location.ashen.station",
             hint="Hask has been watching the plateau road since the herds went quiet. Optional.",
             journal="Hask saw a rider in black iron stop at the foot of the ramp and ride back down."),
        kill("enemy.beast_lord", 1, "Defeat the Beast Lord on his plateau", tag="lord", location="location.ashen.beast_lair",
             hint="Light the blighted brazier at the head of the ramp. In the second phase he calls his pack, and in the third the scar answers.",
             journal="The Beast Lord fell, and the Wilds went quiet the way a forest does when the hunter has gone."),
        talk("dialogue.ashen_headwoman", "Tell Maeve the plateau is quiet", tag="maeve", location="location.ashen.last_hearth",
             req_flag="flag.beast_lord_defeated", hint="Maeve is at the fence at Last Hearth.",
             journal="Maeve said she may sleep a night through for the first time in thirty years."),
    ])

# --- placed interactables ------------------------------------------------------------------------------------

TRACK_A = clue(
    "beast_track_a", "A Staked Track",
    "Hoofprints of an ashfall elk, deep and even, cow and calf, walking. Over them, a pace behind and to the side, the prints "
    "of a dire wolf, also walking. The wolf's stride has not once broken to chase. A third set joins at the next stake: the "
    "broad splayed track of a thornback boar. They pace each other like a family on a road home.",
    "Note it in the tally.", "Hask's red stake stands in the ash. The tracks go on without you.")

TRACK_B = clue(
    "beast_track_b", "A Staked Track",
    "The second track is old enough to have drifted. Elk and wolf again, and now a litter of maw tracks beside them: long "
    "toed, wrong, the prints of wolves that were something else once. They keep station with the elk. Every print points "
    "the same way, north-east, towards the wall. You find fur snagged in a thorn at one height that is neither wolf nor "
    "elk but both, as if one animal had worn two coats. Ash lies in every print like salt.",
    "Note it in the tally.", "The second stake is leaning a little. The tracks beside it have not changed.")

TRACK_C = clue(
    "beast_track_c", "A Staked Track",
    "The third track is a road. Hundreds of prints, trampled to a cut a pace wide and hand-deep. At the edge where the "
    "tracks leave the scar, something has been laid in the dust: not a bone, not a pelt, but a ring of small stones set "
    "around a single antler, the way a person sets a table. The beasts did not do that. Or they did, and that is worse.",
    "Leave the ring undisturbed.", "The ring of stones around the antler has not moved. The road leads on, north.")

HERD = Dialogue(
    id="dialogue.ashen_herd_choice", speaker="The Herd", start="root",
    start_variants=[StartVariant(has_flag("flag.beat.herd_decided"), "after")],
    nodes=[
        Node("root", "In a shallow of ash stands the herd: forty animals of six kinds, shoulder to shoulder, not grazing, not "
                     "sleeping. At their heart a grey-eyed elk cow looks at you with perfectly ordinary elk eyes, and "
                     "behind her a great dire wolf lies in the ash like a hound at a hearth. Antlers and fangs point "
                     "nowhere. They are waiting for something to tell them to go on. The ember in your chest tugs at "
                     "you, as though it knew the voice.",
             [go("Cull the herd before it reaches him.", "slay", tag="slay", do=(E.SET_FLAG, "flag.fork.herd_slain"),
                 do2=rep("faction.ash_hunters", 10)),
              go("Burn three healing herbs and break the call.", "calm", tag="calm",
                 when=has_item("item.material.healing_herb", 3), do=(E.SET_FLAG, "flag.fork.herd_calmed"),
                 do2=take("item.material.healing_herb", 3)),
              leave("Not yet.", tag="bye")]),
        Node("slay", "You do not pretend to speak to them. You draw, and the herd, which has been waiting for a voice, "
                     "hears one: yours. They come at you in a wave with no fear in it, and it is the absence of fear "
                     "that you will remember. When the ash settles there is a trampled shallow, a smell of copper, and "
                     "an elk calf standing untouched among the dead, looking at you without understanding. Hask will want "
                     "the count.",
             [go("Gather what the Hunters will pay for.", "slay_pelts", tag="pelts", do=give("item.material.beast_pelt", 4))],
             on_enter=(E.ADD_CORRUPTION, "3")),
        Node("slay_pelts", "Four good pelts, cut clean. You tell yourself that the Hunters keep records, and that records "
                           "are a kind of mercy. The calf is still watching.",
             [leave("Climb back down.", tag="go")]),
        Node("calm", "You drop the herbs on the coals and green-grey smoke rolls out sweet across the shallow. It is nothing "
                     "the Beast Lord made and nothing he can unmake. The herd's heads come up one by one. The cow elk "
                     "breathes it in, blinks, and for the first time in a month looks like an animal. The wolf sneezes. "
                     "Ten breaths later the whole herd has simply stopped listening. A dire wolf lies down twenty paces "
                     "from a calf and does not so much as look at it. Somewhere up the plateau, something notices that "
                     "its voice has gone quiet.",
             [go("Watch the smoke.", "calm_end", tag="watch", do=rep("faction.ash_hunters", -5))],
             on_enter=(E.ADD_CORRUPTION, "-3")),
        Node("calm_end", "Hask will not like it. The Hunters' manual has a page for culling and a page for tracking and no "
                         "page at all for smoke. You will have to hold the coals while the herd settles.",
             [leave("Keep the coals alive.", tag="go")]),
        Node("after", "The herd has made its answer in the ash. It does not need you to ask it again.",
             [leave("Leave it be.", tag="bye")]),
    ])

DIALOGUES = [TRACK_A, TRACK_B, TRACK_C, HERD]

LOCALE = [
    ("ch.2.ashen", "Act II: Last Hearth"),
    ("chapter.ch.2.ashen.title", "Act II: Last Hearth"),
    ("chapter.ch.2.ashen.subtitle", "Something calls the beasts of the Ashen Wilds, and eleven survivors are in its way."),
    ("boss.beast_lord.epithet", "He Who Let It In"),
    ("boss.beast_lord.intro", "'Do you remember a name? Say it. It has been so long since anyone said it.'"),
]


def build():
    return [Q13, Q14, Q15], DIALOGUES, LOCALE, "spec1"
