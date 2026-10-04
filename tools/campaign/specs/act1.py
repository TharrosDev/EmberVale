"""W-Spec-1, Act I: Embers at the Crown (missions 01 to 09). See docs/playbook/campaign.md.

New quests: smoke_over_the_square, the_pass_kept, crossway_whispers, citadel_approach, iron_king.
Patched legacy quests (append-only): warband.bounty, forge, remedies, heart.
Fork F1 (Warden's Mercy): flag.fork.dray_spared / flag.fork.dray_pressed, written by dialogue.dray.
Ashen Knight beats A1 (cairn glimpse), A2 (crate sigil), A3 (dialogue.rival_arena).
"""

from tools.campaign.model import (
    C, E, K, Dialogue, Node, Patch, Quest, StartVariant, defend, escort, go, has_flag, interact, kill, leave, milestone,
    reach, say, set_flag, talk)
from tools.campaign.specs._s1 import (
    CH1, CROWN, clue, give, has_item, rep, seen, take)

# Flags written by code, a scene or a story rule (data/story/rules/spec1.json), not by any .tres.
CODE_FLAGS = {
    "flag.main.opening_done": "story rule rule.opening_done (OpeningFinishedEvent), data/story/rules/spec1.json",
    "flag.beat.armed_by_known": "story rules rule.armed_* (honest, broker or heist path), data/story/rules/spec1.json",
    "flag.beat.dray_decided": "story rules rule.dray_decided_*, data/story/rules/spec1.json",
    "flag.party.companion.kael": "StoryRuleDirector party mirror (flag.party.<companion id> while recruited and in the party)",
}

# ---------------------------------------------------------------------------------------------------
# Quests
# ---------------------------------------------------------------------------------------------------

Q01 = Quest(
    id="quest.main.smoke_over_the_square",
    title="Smoke over the Square",
    summary="Black smoke stands over the north road and the Elder has stopped calling it a hearth fire. Ring the alarm "
            "bell, hold the square, and learn why a goblin warband is marching in daylight.",
    detail="Goblins raid at night, in threes, and run when they bleed. This warband walked up the Kingsway in daylight, in "
           "step. The Elder wants the old bell by the Waystone rung and the square held until the town has barred its "
           "doors. Whoever is giving the orders has put a warband at the gate, and the town has you and a well.",
    chapter_key=CH1, order=1, region=CROWN, level=1, giver_key=K("dlg.elder.speaker"),
    auto_start="flag.main.opening_done", completion_flag="flag.main.smoke_over_the_square_done",
    sequential=True, xp=100, gold=30, reward_items=[("item.potion.health", 1)],
    objectives=[
        talk("dialogue.elder", "Hear the Elder out", tag="warn", location="location.ember_crown.town",
             hint="The Elder keeps to the Crown Square: the well and the Waystone by morning, the forge later. The compass finds him.",
             journal="The Elder pointed north at the smoke and told you to ring the bell."),
        interact("interact.ember_crown.alarm_bell", "Ring the alarm bell by the Waystone", tag="bell",
                 location="location.ember_crown.town", completion_flag="flag.beat.alarm_rung",
                 hint="The bell hangs from a post beside the Waystone, in the middle of the square.",
                 journal="You rang the bell. Shutters banged shut across the square."),
        milestone("flag.beat.carter_helped", "Help the wounded carter indoors", tag="carter", optional=True,
                  location="location.ember_crown.town", req_flag="flag.beat.alarm_rung",
                  hint="A carter lies against the trough at the north edge of the square. Optional.",
                  journal="You got the carter under a roof before the second wave."),
        defend("location.ember_crown.town", 45, "Hold the square against the raiders", tag="hold",
               req_flag="flag.beat.alarm_rung", completion_flag="flag.beat.square_held",
               hint="Stay near the well and the Waystone and cut down what reaches you. Holding your ground is what counts.",
               journal="The raiders broke on the stones of the square and ran."),
        talk("dialogue.elder", "Tell the Elder it is over", tag="report", req_flag="flag.beat.square_held",
             location="location.ember_crown.town", hint="Go back to the Elder in the Crown Square. The compass finds him.",
             journal="The Elder turned a black-iron token over in his fingers and did not like it."),
    ])

Q02 = Quest(
    id="quest.main.the_pass_kept",
    title="The Pass Kept",
    summary="Twelve years ago Kael Aldemar's sword-brother held the old Ashfall pass so the Emberguard could run. Black "
            "iron on a raider has sent you to look at it. Walk out to the Ashen Breach, see what was left there, and "
            "take it to Kael.",
    detail="The Emberguard held a cut in the eastern hills until the ash came down from the Wilds. Kael ran. "
           "Toren did not. The goblins took everything of his but the name, and somebody has kept his cairn ever since. "
           "The Citadel's scrip is stamped square. Something at that cairn is not.",
    chapter_key=CH1, order=2, region=CROWN, level=1, giver_key=K("companion.kael.name"),
    auto_start="flag.main.smoke_over_the_square_done", completion_flag="flag.main.the_pass_kept_done",
    sequential=True, xp=120, gold=40,
    objectives=[
        reach("location.ember_crown.ashen_breach", "Walk out to the Ashen Breach, the old Ashfall pass", tag="pass",
              hint="East of the capital, in the hills past the mine road: look for the burnt cut and follow the dead grass.",
              journal="The Breach is a wound in the hills. The old road runs through it and ends in sky."),
        escort("companion.kael", "location.ember_crown.ashen_breach", "Walk Kael out to the Breach", tag="kael_escort",
               optional=True, req_flag="flag.party.companion.kael",
               hint="Optional. Kael is in your party: keep him with you on the walk and he will come to the cut. Do not leave him behind.",
               journal="Kael walked the old road beside you and stopped at the mouth of the cut without being asked."),
        interact("interact.ashen_breach.toren_cairn", "Find Toren's cairn in the cut", tag="cairn",
                 location="location.ember_crown.ashen_breach",
                 hint="The cairn stands near the rubble heap at the western mouth of the cut.",
                 journal="Black stones, stacked shoulder high, and a name cut under a buckle: TOREN. HE HELD."),
        milestone("flag.beat.black_token_taken", "Take the black-iron token from the cairn", tag="token",
                  location="location.ember_crown.ashen_breach",
                  hint="It lies on the cap-stone once you have read the cairn. If the stone is bare, read the cairn again.",
                  journal="You took a round black token with a ring cut into it, broken at the top."),
        talk("dialogue.kael", "Show the token to Kael Aldemar", tag="kael", location="location.ember_crown.town",
             hint="Kael is in the Crown Square, sharpening a blade that does not need it.",
             journal="Kael would not keep the token. He thinks it was left for you."),
    ])

Q04 = Quest(
    id="quest.main.crossway_whispers",
    title="Whispers at the Crossway",
    summary="Every cart that goes north passes the Wardens' post at the Crossway, and its captain has been told not to "
            "open the ones sealed for the Citadel. Walk the road north, hear Captain Fenn out, and open what he cannot.",
    detail="Arms do not fall from the sky. Someone forged the spearheads the raiders carried, someone carted them north "
           "under seal, and someone at the Crossway counted them through. Captain Fenn has twelve wardens for a road "
           "that needs forty and a letter forbidding him to look in the wrong crate. There is a crate in the impound "
           "that nobody is allowed to open. You are not a warden.",
    chapter_key=CH1, order=4, region=CROWN, level=3, giver_key=K("dlg.dawnwarden_captain.speaker"),
    auto_start="flag.main.bounty_done", completion_flag="flag.main.crossway_whispers_done",
    sequential=True, xp=150, gold=60,
    objectives=[
        reach("location.crossway.post", "Reach the Wardens' post at the Crossway", tag="post",
              hint="North up the Kingsway from the capital, past the north moor, to the gate with the towers.",
              journal="The Crossway post: two towers, a gate, and a road that everything north has to use."),
        talk("dialogue.dawnwarden_captain", "Show Captain Fenn the quartermaster's stamp", tag="fenn",
             location="location.crossway.watch", completion_flag="flag.beat.fenn_briefed",
             hint="Fenn keeps the gate ledger by the door of the keep.",
             journal="Fenn said he could not cut the seal on the impounded crate himself. He did not say you could not."),
        interact("interact.crossway.impound_crate", "Open the sealed crate at the Impound Counter", tag="crate",
                 location="location.crossway.impound", completion_flag="flag.beat.crate_opened",
                 hint="The Impound Counter is at the east end of the post, beyond the Search Table, with the clerk standing over it.",
                 journal="Forty unfinished spearheads in garrison straw, each stamped twice: the Citadel's square, and a ring with a gap."),
        milestone("flag.beat.armed_by_known", "Find out who signed for the warband's arms", tag="armed",
                  location="location.crossway.watch",
                  hint="Take the stamps back to Fenn, or buy the reading from the Syndicate broker at Hollowreach, or slip the carter ledger at the Search Table.",
                  journal="The quartermaster's mark is the Marshal's. Now you know whose name is on the arms."),
    ])

Q07 = Quest(
    id="quest.main.citadel_approach",
    title="Under the Iron Banner",
    summary="The Iron King's garrison sits on the spur above the capital, and the Marshal who signs for its arms has not "
            "been seen in a year. Climb to the Citadel, put out the signal fire before it calls the rest, and learn "
            "whether Orsolo Dray is a traitor or a prisoner.",
    detail="The Citadel has two ways in: the front ramp, which is watched, and a postern in the west wall that a "
           "wounded scout may have mentioned. The brazier at the ramp keeps burning because it is a signal; every pair "
           "of sentries you cut down is replaced from the garrison hall until its smoke goes out. In a stockade pen "
           "in the court sits the man whose name is on the arms, and what you do with him is yours to answer for.",
    chapter_key=CH1, order=7, region=CROWN, level=4, giver_key=K("dlg.dawnwarden_captain.speaker"),
    auto_start="flag.main.remedies_done", completion_flag="flag.main.citadel_approach_done",
    sequential=True, xp=200, gold=80, reward_items=[("item.potion.health", 2)],
    objectives=[
        reach("location.ember_crown.iron_citadel", "Climb to the Iron Citadel's ramp", tag="ramp",
              hint="The Kingsway climbs to the spur from the capital's north gate. Keep to the road until you see the braziers.",
              journal="The ramp is lined with black banners. Two braziers burn on it, and one of them is not for warmth."),
        kill("enemy.soldier", 6, "Cut down the sentries at the ramp", tag="sentries", location="location.ember_crown.iron_citadel",
             forbid_flag="flag.beat.postern_known",
             hint="They stand in pairs and relight the signal when hurt. Fight on the ramp, not in the open.",
             journal="The ramp sentries are down. Their smoke still climbs."),
        kill("enemy.soldier", 3, "Cut down the sentries at the postern", tag="postern",
             location="location.ember_crown.iron_citadel", req_flag="flag.beat.postern_known",
             hint="The scout's tip thins the guard: only three stand between you and the court. Her drain door is in the west wall.",
             journal="You came in by the drain door and the court was half empty."),
        interact("interact.citadel.signal_brazier", "Douse the signal brazier", tag="brazier",
                 location="location.ember_crown.iron_citadel", completion_flag="flag.beat.signal_doused",
                 hint="The west ramp brazier is the one that never goes out. Put it out and the replacements stop.",
                 journal="The signal is out. Nothing is coming up the ramp behind you."),
        talk("dialogue.dray", "Find Marshal Orsolo Dray in the garrison hall", tag="dray",
             location="location.ember_crown.iron_citadel",
             hint="Dray is held in a wooden stockade pen in the Citadel court, east of the court well.",
             journal="Dray is chained in a stockade pen in the court. He signed for the arms until the day he stopped."),
        milestone("flag.beat.dray_decided", "Decide what becomes of the Marshal", tag="decide",
                  location="location.ember_crown.iron_citadel",
                  hint="Cut his chains or press him for what he knows. Speak to him again if you walked away.",
                  journal="You settled it. Whatever it was, you will be asked about it again."),
    ])

Q09 = Quest(
    id="quest.main.iron_king",
    title="The Iron King",
    summary="The warband is broken, and every scrip, ledger and confession points up the long road to the Ember Arena, "
            "where the first Flamebearer keeps his court of iron. Light the brazier, end it, and find out who has been "
            "watching from the gallery.",
    detail="The Iron King was the first of the seven to be given the fire, and he has had a long time to decide what "
           "it is for. He holds the arena the way he held the Kingsway: closed to everyone but his own. The brazier "
           "at the sand's edge calls him out. Afterwards the Elder will want to hear all of it, including whatever "
           "you were not expecting.",
    chapter_key=CH1, order=9, region=CROWN, level=6, giver_key=K("dlg.elder.speaker"),
    auto_start="flag.frostfang.passage_open", completion_flag="flag.main.iron_king_done",
    one_shot=True, sequential=True, xp=400, gold=150, reward_items=[("item.potion.health", 2)],
    objectives=[
        kill("enemy.iron_king", 1, "Defeat the Iron King in the Ember Arena", tag="king",
             location="location.ember_crown.arena",
             hint="Light the challenge brazier at the edge of the sand. In the second phase the garrison comes down to him: use the pillars.",
             journal="The Iron King fell, and his ember lay on the sand like a second sun."),
        milestone("flag.rival.met1", "Speak to the rider in the arena gallery", tag="rival",
                  location="location.ember_crown.arena",
                  hint="A rider in black iron sits in the royal box above the sand. Walk up the gallery stair.",
                  journal="A tall rider in black iron watched the whole fight and spoke to you after."),
        talk("dialogue.elder", "Return to the Elder with what you learned", tag="elder",
             location="location.ember_crown.town",
             hint="The Elder is in the Crown Square. He will tell you where the Flamebearers went.",
             journal="The Elder sang a counting rhyme about a stair, five who fell, and one who stayed to kneel."),
    ])

# Patches to the four hand-authored warband quests: append objectives and fill empty fields only.
P_BOUNTY = Patch(
    "quest.warband.bounty", auto_start="flag.main.the_pass_kept_done", completion_flag="flag.main.bounty_done",
    append=[milestone("flag.beat.banner_burned", "Burn the warband's banner at the goblin camp", tag="banner",
                      location="location.wilds.north",
                      hint="The camp is in the broken ruin on the western side of the Northern Wilds, a short walk from the Deadfall Lodge. The banner hangs from a pole of lashed deadfall.",
                      journal="The banner burned. Its corner carried the Citadel quartermaster's stamp.")],
    fields={"ChapterKey": CH1, "OrderInAct": 3, "RegionId": CROWN, "RecommendedLevel": 2,
            "GiverNameKey": "dlg.guild_board.speaker", "DetailKey": "quest.warband.bounty.detail"})

P_FORGE = Patch(
    "quest.warband.forge", auto_start="flag.main.crossway_whispers_done", completion_flag="flag.main.forge_done",
    append=[talk("dialogue.smith", "Bring the iron to Bryn", tag="handin", location="location.ember_crown.smith",
                 hint="Bryn is at the Iron Anvil in the Crown Square. He wants the ore in your hands, not in a ledger.",
                 journal="Bryn lit the forge from your ore: a forge that answers to the town, not the spur.")],
    fields={"ChapterKey": CH1, "OrderInAct": 5, "RegionId": CROWN, "RecommendedLevel": 3, "SequentialObjectives": True,
            "GiverNameKey": "dlg.smith.speaker", "DetailKey": "quest.warband.forge.detail"})

P_REMEDIES = Patch(
    "quest.warband.remedies", auto_start="flag.main.forge_done", completion_flag="flag.main.remedies_done",
    append=[milestone("flag.beat.postern_known", "Tend the wounded scout on the King's Approach", tag="scout",
                      optional=True, location="location.ember_crown.iron_citadel",
                      hint="A Dawnwarden scout lies in the ditch where the Kingsway climbs to the Citadel. She needs a health potion. Optional.",
                      journal="The scout told you about a drain door in the Citadel's west wall."),
            talk("dialogue.apothecary", "Bring the herbs to Mirela", tag="handin", location="location.ember_crown.apothecary",
                 hint="Mirela keeps the Green Retort in the Crown Square. Four sprigs, in your hand.",
                 journal="Mirela crushed one sprig, breathed it in, and told you to come back alive.")],
    fields={"ChapterKey": CH1, "OrderInAct": 6, "RegionId": CROWN, "RecommendedLevel": 3, "SequentialObjectives": True,
            "GiverNameKey": "dlg.apothecary.speaker", "DetailKey": "quest.warband.remedies.detail"})

P_HEART = Patch(
    "quest.warband.heart", auto_start="flag.main.citadel_approach_done",
    append=[reach("location.ember_crown.arena", "Follow the warband's road to the Ember Arena", tag="arena",
                  hint="The arena road leaves the mine road and runs north to the arena gate. The black-iron scrip stops there.",
                  journal="The warband's road ends at the arena gate, and the gate is open.")],
    fields={"ChapterKey": CH1, "OrderInAct": 8, "RegionId": CROWN, "RecommendedLevel": 5, "SequentialObjectives": True,
            "GiverNameKey": "dlg.elder.speaker", "DetailKey": "quest.warband.heart.detail"})

# ---------------------------------------------------------------------------------------------------
# Dialogues (generated): placed interactables, the captive marshal, the rider in the gallery
# ---------------------------------------------------------------------------------------------------

BELL = clue(
    "alarm_bell", "The Alarm Bell",
    "The bell hangs from a post beside the Waystone, bronze gone green at the lip, its rope greasy with three generations "
    "of hands. You haul it. It cracks across the square and does not stop. Doors slam. Somewhere a mother is shouting "
    "a name. Far up the Kingsway the smoke turns towards the sound.",
    "Brace.", "The rope is still swinging. Nobody is going to touch it again today.")

CARTER = Dialogue(
    id="dialogue.wounded_carter", speaker="Joss Harrow, Carter", start="root",
    start_variants=[StartVariant(has_flag("flag.beat.carter_helped"), "after")],
    nodes=[
        Node("root", "A carter sits against the trough with both hands clamped on his thigh, his cart on its side behind "
                     "him. 'Joss Harrow. They took the mule and left me the cart, which is the wrong way round.' He "
                     "tries a laugh and it comes out as a cough. 'If somebody could get me off the stones before the "
                     "next lot arrive, I would name a mule after them.'",
             [set_flag("Get an arm under him and carry him to the inn.", "flag.beat.carter_helped", "helped",
                       tag="help", do2=rep("faction.villagers", 5)),
              leave("There is no time. Stay low.", tag="bye")]),
        Node("helped", "He is heavier than he looks and complains the whole way, which you take as a good sign. The "
                       "innkeeper's wife pulls him in by the collar and the door bars behind him. 'Mule,' he calls "
                       "through the shutter. 'I meant it about the mule.'",
             [leave("Back to the square.", tag="go")]),
        Node("after", "The shutter opens a crack. 'Still no mule,' says Joss, 'but I am told you have a good chance of "
                      "living long enough to be paid in one.'", [leave("Rest.", tag="bye")]),
    ])

TOREN_CAIRN = clue(
    "toren_cairn", "A Cairn of Black Stones",
    "Black stones, stacked shoulder high with no mortar and no gaps. Whoever built this had done it before, and more than "
    "once. A sword-belt buckle is set in the cap-stone, and cut beneath it with a dagger point, in letters gone soft "
    "with twelve winters of wind: TOREN. HE HELD. The grass around the cairn has been pulled by hand. Nobody comes out "
    "this far to tend a grave, and somebody does.",
    "Take your hand off the stone.", "The cairn is quiet. The wind has the pass to itself.")

BLACK_TOKEN = Dialogue(
    id="dialogue.black_token", speaker="A Black-Iron Token", start="root",
    start_variants=[StartVariant(has_flag("flag.beat.black_token_taken"), "after")],
    nodes=[
        Node("root", "A disc of black iron the size of your palm lies on the cap-stone, set square as if with a rule. "
                     "It is not Citadel scrip: those are stamped square and this is round, and where the Citadel's marks "
                     "are crisp this one has been rubbed smooth by a thumb. Cut across its face is a ring, broken at "
                     "the top like a door left ajar. It is cold in a way the wind cannot explain.",
             [go("Take the token.", "taken", tag="take", do=(E.SET_FLAG, "flag.beat.black_token_taken")),
              leave("Leave it where it lies.", tag="bye")]),
        Node("taken", "It is heavier than it should be. As your fingers close, the light on the ridge above the cut "
                      "changes: a figure in black iron stands against the sky, utterly still, his helm turned towards "
                      "the cairn, one hand on the pommel of a sword too long for any man to carry. He does not wave "
                      "and he does not hide. You blink, and there is only ash light and bent grass. Whoever he is, he "
                      "put this here, and he watched you pick it up.",
             [leave("Take it back to Kael.", tag="go")], on_enter=(E.SET_FLAG, "flag.rival.glimpsed")),
        Node("after", "The cap-stone is bare now, and paler where the token lay.", [leave("Leave it be.", tag="bye")]),
    ])

BANNER = Dialogue(
    id="dialogue.warband_banner", speaker="The Warband's Banner", start="root",
    start_variants=[StartVariant(has_flag("flag.beat.banner_burned"), "after")],
    nodes=[
        Node("root", "A pole of lashed deadfall with a banner of stitched goblin hide hanging from it, stiff with old "
                     "blood. Goblins do not make banners. In the lower corner, sewn on with sinew, is a square patch of "
                     "black-iron mail with a stamp pressed into it: the Citadel quartermaster's mark, the same one "
                     "that was on the Elder's token.",
             [go("Put a torch to it.", "burned", tag="burn", do=(E.SET_FLAG, "flag.beat.banner_burned")),
              leave("Leave it. Count the camp first.", tag="bye")]),
        Node("burned", "The hide curls and spits. The Citadel's stamp blackens last. Whoever this camp answers to will "
                       "know by dusk that someone has been here, and what they looked at. The cart ruts at the camp's "
                       "edge all run north, towards the Crossway, where every cart bound for the Citadel passes.",
             [leave("Back to your work.", tag="go")]),
        Node("after", "Ash on the ground and a charred pole. The camp's goblins keep well away from the spot.",
             [leave("Leave it be.", tag="bye")]),
    ])

IMPOUND_CRATE = clue(
    "impound_crate", "The Sealed Crate",
    "The wax on the lid is the Citadel quartermaster's, square and red, and the clerk looks away while you work the "
    "chisel under it. Inside, in garrison straw, lie forty spearheads of unfinished black iron. The manifest tied to the "
    "lid says STORES, GARRISON. Each socket carries two stamps: the Citadel's square, and under it a ring with a gap at "
    "the top. The ring is not in any manifest.",
    "Memorise the marks and close the lid.", "The lid is back on the crate, and the wax is not going to look the same again.",
    do=(E.SET_FLAG, "flag.rival.sigil"), do2=(E.SET_FLAG, seen("impound_crate")))

CARTER_LEDGER = Dialogue(
    id="dialogue.carter_ledger", speaker="The Carter Ledger", start="root",
    start_variants=[StartVariant(has_flag("flag.beat.armed_heist"), "after")],
    nodes=[
        Node("root", "The Search Table's carter ledger lies open under a paperweight made from a worn spur. Every "
                     "consignment through the gate for a year, in the clerk's cramped hand, including the Citadel wax "
                     "loads and the signature that cleared them. The clerk is arguing with a drover about a toll and has "
                     "his back to you. The page you want is already half loose.",
             [go("Tear out the page and pocket it.", "taken", tag="take", do=(E.SET_FLAG, "flag.beat.armed_heist"),
                 do2=rep("faction.dawnwardens", -8)),
              leave("Put the spur back. Not like this.", tag="bye")]),
        Node("taken", "The sound is smaller than you feared. The page folds into your sleeve, and the quartermaster's "
                      "signature on it is Orsolo Dray's. The page also lists a year of Citadel factors carting Crown iron north; "
                      "Bryn at the Iron Anvil will want to hear about that. The drover wins his argument about the toll, "
                      "which is the most anyone at the Crossway will notice today.",
             [leave("Walk away at a normal pace.", tag="go")], on_enter=rep("faction.iron_syndicate", 3)),
        Node("after", "The ledger lies open to a page with a ragged edge. The clerk has not noticed yet.",
             [leave("Leave it be.", tag="bye")]),
    ])

WOUNDED_SCOUT = Dialogue(
    id="dialogue.wounded_scout", speaker="Ilse Marrow, Scout", start="root",
    start_variants=[StartVariant(has_flag("flag.beat.postern_known"), "after")],
    nodes=[
        Node("root", "A Dawnwarden scout lies in the ditch below the Kingsway with a bolt through her calf and a "
                     "cloak pulled over her head like a tent. 'Not a patrol,' she whispers. 'Do not stand up. They are "
                     "still watching the road from the spur.' She looks at what you carry. 'If you have a draught to "
                     "spare, I have something to trade you for it. A door the Citadel forgot.'",
             [go("Give her a health potion.", "healed", tag="heal", when=has_item("item.potion.health", 1),
                 do=take("item.potion.health", 1), do2=(E.SET_FLAG, "flag.beat.postern_known")),
              leave("I have nothing to spare. Stay hidden.", tag="bye")]),
        Node("healed", "She drinks it like water and the colour comes back to her face by degrees. 'West wall of the "
                       "Citadel, under the third buttress. There is a drain door for the garrison kitchens. The King "
                       "forgot it because kings do not think about drains. Three sentries between it and the court, "
                       "not six.' She tucks the cloak back over her head. 'Ilse Marrow. If you live, tell Fenn I "
                       "kept my post.'",
             [leave("Rest. I will tell him.", tag="go")], on_enter=rep("faction.dawnwardens", 3)),
        Node("after", "'Third buttress, west wall,' she whispers, and goes back to being a lump in a ditch.",
             [leave("Keep your head down.", tag="bye")]),
    ])

DRAY = Dialogue(
    id="dialogue.dray", speaker="Marshal Orsolo Dray", start="root",
    start_variants=[StartVariant(has_flag("flag.fork.dray_spared"), "spared_after"),
                    StartVariant(has_flag("flag.fork.dray_pressed"), "pressed_after")],
    nodes=[
        Node("root", "A man in a marshal's coat with the rank cords torn off sits chained to a post in a wooden stockade pen "
                     "in the Citadel court, straw under him. His boots are good, and he has not seen a razor in a month. 'You "
                     "came up the ramp alone and the brazier is out. Either you are very good, or someone wants me to "
                     "think so.' He rattles the chain. 'Orsolo Dray. I signed what they put in front of me for eleven "
                     "years, and when they put the spearheads in front of me I stopped, and this is what stopping buys.'",
             [go("Who is paying the warband?", "who", tag="who"),
              go("What do you know about the ring with a gap?", "ring", tag="ring"),
              go("Cut his chains.", "spared", tag="spare", do=(E.SET_FLAG, "flag.fork.dray_spared"),
                 do2=rep("faction.dawnwardens", 10)),
              go("Press him for what he knows.", "pressed", tag="press", do=(E.SET_FLAG, "flag.fork.dray_pressed"),
                 do2=(E.ADD_CORRUPTION, "4")),
              go("Show your Dawnwarden badge and swear him to the Watch.", "spared_watch", tag="spare_watch",
                 when=(C.GUILD_RANK_AT_LEAST, "faction.dawnwardens:0"), do=(E.SET_FLAG, "flag.fork.dray_spared"),
                 do2=rep("faction.dawnwardens", 18)),
              go("Offer his name to the Syndicate's ledger.", "pressed_syndicate", tag="press_syndicate",
                 when=(C.GUILD_RANK_AT_LEAST, "faction.iron_syndicate:0"), do=(E.SET_FLAG, "flag.fork.dray_pressed"),
                 do2=rep("faction.iron_syndicate", 12)),
              leave("I am not done yet. Wait.", tag="bye")]),
        Node("who", "'The King. Not for love, for use. A king who keeps a warband on his own roads has an excuse to close "
                    "the Kingsway to everyone else and take the tolls himself. The goblins get iron, he gets a siege he "
                    "can ration. The captains drink at the arena's side gate: cut those and he has no road.' He laughs "
                    "without breath. 'Nothing in this Citadel has been about the realm for ten years. It is about the "
                    "King's weather.'",
             [go("What do you know about the ring with a gap?", "ring", tag="ring"), go("Back.", "root", tag="back")]),
        Node("ring", "'I do not know the ring. I know the Citadel does not make it. I know the quartermaster's strongbox "
                     "has a drawer I was never given the key to, and a clerk who never signs and never leaves. The "
                     "King will not let anyone open the drawer.' His eyes slide away. 'That is all, and it is not much.'",
             [go("Back.", "root", tag="back")]),
        Node("spared", "The lock is old and the key hangs on a nail where the guards could not be bothered to hide it. "
                       "The chain drops. Dray stands, and his knees almost betray him, and he does not let them. 'I "
                       "will not say I am grateful. I will give you the roster, the postern drain and the arena's side "
                       "gate, and I will tell the Black Wardens' captain that his marshal has been summoned to the "
                       "Crossway to answer for the arms. Half of them will believe it. The other half will find they "
                       "have somewhere else to be.' He walks out of the hall as if he owned it, which for a moment he "
                       "does.",
             [leave("Go well, Marshal.", tag="go")]),
        Node("pressed", "You do not cut the chain. You stand in the doorway, close enough that he has to look at what "
                        "burns behind your eyes. You ask for the strongbox key. He says nothing. You ask for the roster, "
                        "and say something else, quietly, and he gives you all of it in the order you asked, and a "
                        "few things you did not: the postern drain, and the arena's side gate where the captains drink. "
                        "When he has done he does not look at you. 'The Wardens will come when "
                        "the brazier does not answer. I will tell them I was no use to you.'",
             [go("Take the strongbox key and the Citadel's purse.", "pressed_purse", tag="take",
                 do=give("item.currency.gold", 250))],
             on_enter=rep("faction.dawnwardens", -10)),
        Node("pressed_purse", "The drawer opens on the third try. The purse is heavy, and what is under it is only "
                              "paper. You leave him the chain.", [leave("Climb back down.", tag="go")]),
        Node("spared_watch", "You hold the badge where the lamp can find it, and the Marshal reads the sigil twice as if "
                             "it might be a trick. 'The Watch does not take a man who signed for goblin spears.' 'The Watch "
                             "takes a man who stopped signing,' you say, and turn the key on the nail. He does not weep. He "
                             "stands very straight and says the Watch's oath into the straw of the pen, all of it, "
                             "including the part about the weak. Then he walks out of the hall with his chain over one "
                             "shoulder like a stole, and the sentries who never came do not come now.",
             [leave("Go well, Captain.", tag="go")]),
        Node("pressed_syndicate", "You do not touch the chain. You say a number, and then a name, and Dray's face changes "
                                  "because the number is what the Ledger House pays for a marshal's confession and the name "
                                  "is yours. 'You are the Syndicate's now,' he says, with a sort of tired wonder. 'Good. At "
                                  "least they keep books.' He gives you the roster, the drain door, the strongbox key and the arena's side "
                                  "gate, in that order, and keeps his eyes on the straw while he does it. The Ledger House "
                                  "will hear before the Watch does.",
             [go("Take the key and the Citadel's purse.", "pressed_purse", tag="take", do=give("item.currency.gold", 150))],
             on_enter=rep("faction.dawnwardens", -10)),
        Node("spared_after", "The pen is empty. The chain lies in a pile where it fell, and the post "
                             "has a bright new scar where the lock was forced.", [leave("Leave.", tag="bye")]),
        Node("pressed_after", "Dray studies the straw. 'Whatever you wanted from me, you have it,' he says to the "
                              "floor. 'Close the gate on your way out.'", [leave("Leave.", tag="bye")]),
    ])

RIVAL_ARENA = Dialogue(
    id="dialogue.rival_arena", speaker="The Rider in Black Iron", start="root",
    start_variants=[StartVariant(has_flag("flag.rival.met1"), "after")],
    nodes=[
        Node("root", "In the royal box above the sand, a rider in black iron has sat through the whole of it. He rises as "
                     "you look, and the stone seat does not creak under him. He is very tall. His visor is down, and "
                     "behind it the air is the colour of old fire.",
             [go("Wait for him to speak.", "speaks_taken", tag="wait_taken",
                 when=has_flag("flag.iron_king_absorbed")),
              go("Wait for him to speak.", "speaks_left", tag="wait_left",
                 when=(C.MISSING_FLAG, "flag.iron_king_absorbed")),
              go("Who are you?", "who", tag="who"),
              leave("Say nothing and go.", tag="bye")]),
        Node("who", "'A rider. That will serve for now. I am told it is bad manners to give your name to someone who "
                    "has not yet earned the answer.' He inclines his helm. 'I will give you one thing for nothing: do "
                    "not trust the Elder's rhymes to the word. Every rhyme is somebody's apology.'",
             [go("Wait for him to speak.", "speaks_taken", tag="wait_taken", when=has_flag("flag.iron_king_absorbed")),
              go("Wait for him to speak.", "speaks_left", tag="wait_left",
                 when=(C.MISSING_FLAG, "flag.iron_king_absorbed"))]),
        Node("speaks_taken", "'You took it.' The voice is level and not unkind. 'Everyone does. It is the first fire and "
                             "the oldest of those left, and it goes in like a coal into a dry hearth. You will find it "
                             "keeps its own counsel.' A gauntlet taps the rail. 'He thought a wall around the world "
                             "would keep it whole. He was not wrong about the wall, only about who it was for. We will "
                             "speak again, Seventh. When you are carrying more.'",
             [leave("Watch him go.", tag="go")], on_enter=(E.SET_FLAG, "flag.rival.met1")),
        Node("speaks_left", "'You left it.' There is something in the voice that might be interest, or grief. 'Not many "
                            "do, the first time. It lay there on the sand asking, and you walked past it like an unlit "
                            "lamp.' A gauntlet taps the rail. 'The next ones ask louder. We will speak again, Seventh. "
                            "I would like to see what you do when it is not the first.'",
             [leave("Watch him go.", tag="go")], on_enter=(E.SET_FLAG, "flag.rival.met1")),
        Node("after", "The royal box is empty. On the rail lies a round token of black iron with a ring cut into it, "
                      "broken at the top, and it is warm.", [leave("Leave it be.", tag="bye")]),
    ])

SIGNAL_BRAZIER = clue(
    "signal_brazier", "The Signal Brazier",
    "The west ramp brazier is a basin of black iron on three legs, fed by a pipe from the garrison's own coal store, and "
    "it has not gone out in a year. You kick the feed pipe off its seat and bury the coals under a shield someone left "
    "on the ramp. The smoke thins to a thread and stops. Up on the walls a horn starts, and then stops too, the way a "
    "man stops who has remembered he is the only one still blowing it.",
    "Step back from the ashes.", "The basin is cold and black. Nothing climbs from it now.")

DIALOGUES = [BELL, CARTER, TOREN_CAIRN, BLACK_TOKEN, BANNER, IMPOUND_CRATE, CARTER_LEDGER, WOUNDED_SCOUT, DRAY,
             RIVAL_ARENA, SIGNAL_BRAZIER]

# ---------------------------------------------------------------------------------------------------
# Hand-written locale rows: chapter text, journal details of the patched legacy quests, boss epithets
# ---------------------------------------------------------------------------------------------------

LOCALE = [
    # ChapterKey is an identifier. The generator treats any key-shaped quest field as a locale key, so the identifier
    # itself carries a short row too; the banner reads chapter.<key>.title / .subtitle. The titles carry no "Act N"
    # prefix: the banner prints its own act line from the key, and the tracker shows the title above the quest name.
    ("ch.1", "Embers at the Crown"),
    ("chapter.ch.1.title", "Embers at the Crown"),
    ("chapter.ch.1.subtitle", "Smoke on the north road, a name in black iron, and a king who will not be reasoned with."),
    ("quest.warband.bounty.detail",
     "Goblins have always raided the roads out of the Ember Crown, but never in step and never at noon. The guild pays "
     "three heads. The camp they come from sits in the broken ruin west of the Deadfall Lodge, in the Northern Wilds, and the banner that hangs "
     "there is the first thing in this war that was made by someone who can sew."),
    ("quest.warband.forge.detail",
     "The Citadel's factors bought every ingot in the Crown last spring and never said what for. Bryn's forge has been "
     "cold since. Three lumps of iron from the hills and the town can make its own steel again, with no black in it."),
    ("quest.warband.remedies.detail",
     "The Green Retort is out of everything but complaints. Goblin wounds fill the cots and the Citadel's factors bought "
     "up the linen. Four sprigs of healing herb for Mirela, and if you have a draught to spare, a scout in a ditch below "
     "the Citadel may be worth it."),
    ("quest.warband.heart.detail",
     "A warband that wears Citadel scrip does not break because five goblins died. It breaks when its road ends. "
     "Cut the captains down, then follow the black road to where it ends: the Ember Arena, and whatever holds it."),
    ("boss.iron_king.epithet", "The Black-Iron King"),
    ("boss.iron_king.intro", "'A king answers to no road but his own.'"),
]


def build():
    return [Q01, Q02, Q04, Q07, Q09, P_BOUNTY, P_FORGE, P_REMEDIES, P_HEART], DIALOGUES, LOCALE, "spec1"
