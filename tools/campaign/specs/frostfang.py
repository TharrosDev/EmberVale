"""W-Spec-1, Act II arc 1: Frostfang Reach (missions 10 to 12). See docs/playbook/campaign.md.

closed_hold -> succession (Fork F2, Hjalvar or Halvar) -> storm_tyrant. Fork flags: flag.fork.succession_hjalvar /
flag.fork.succession_halvar, written by dialogue.frostfang_moot_stone. The arc's gate flag.arc.frostfang_ready is
written by a story rule when succession is done (data/story/rules/spec1.json) and read by the Stormcrown brazier.
"""

from tools.campaign.model import (
    C, E, K, Dialogue, Node, Quest, StartVariant, defend, go, has_flag, interact, kill, leave, milestone, reach, talk)
from tools.campaign.specs._s1 import CH2_FROST, FROST, clue, give, rep

CODE_FLAGS = {
    "flag.arc.frostfang_ready": "story rule rule.arc_frostfang (succession done), read by the Stormcrown brazier scene",
    "flag.beat.succession_decided": "story rules rule.succession_decided_*, data/story/rules/spec1.json",
    "flag.beat.stormend_hjalvar": "story rule rule.stormend_hjalvar (Storm Tyrant defeated + Hjalvar's claim), spec1.json",
    "flag.beat.stormend_halvar": "story rule rule.stormend_halvar (Storm Tyrant defeated + Halvar's claim), spec1.json",
    "flag.rival.duel1_won": "BossEncounterDirector, boss.ashen_knight_duel1 (the Knight yields)",
}

Q10 = Quest(
    id="quest.main.closed_hold",
    title="The Closed Hold",
    summary="The clans of Frostfang Reach have shut their hold to the south, and the three scout cairns on the north ridge "
            "have gone dark. The Storm Tyrant's motes ride the high passes. Reach the Clan Hold, draw flint and oil from "
            "Sigrun, relight the cairns, and be standing in the hold when the storm answers.",
    detail="A dark cairn is a hole in the hold's eyes. It is how the vale sees weather coming and how the eight clans know "
           "to wake, and all three went out in the same moon. The Stormbound do not ask for help, and Hjalvar is not "
           "asking now; he is simply failing to turn you away. Sigrun counts oil in lamp-fulls and will not part with it "
           "for nothing, so she will want your name on her tally.",
    chapter_key=CH2_FROST, order=1, region=FROST, level=8, giver_key=K("dlg.clan_chief.speaker"),
    auto_start="flag.main.iron_king_done", completion_flag="flag.main.closed_hold_done",
    sequential=True, xp=250, gold=80, reward_items=[("item.potion.health", 2)],
    objectives=[
        reach("location.frostfang.clan_hold", "Reach the Frostfang Clan Hold", tag="hold",
              hint="The Frostfang door is at the north end of the Ember Crown. Follow the Stormbound Vale road to the hold at its head.",
              journal="The hold's gate was shut. It opened for you, narrowly."),
        talk("dialogue.clan_chief", "Hear Hjalvar Stormbound out", tag="hjalvar", location="location.frostfang.clan_hold",
             completion_flag="flag.beat.hjalvar_briefed",
             hint="Hjalvar sits by the moot ring in the middle of the hold.",
             journal="Hjalvar said the north cairns have gone dark, all three in one moon."),
        talk("dialogue.clan_quartermaster", "Draw cairn oil and flint from Sigrun Ironhand", tag="sigrun",
             location="location.frostfang.clan_hold",
             hint="Sigrun keeps the stores by the long shed. She counts twice.",
             journal="Sigrun counted the oil out twice and wrote your name beside it."),
        interact("interact.frostfang.scout_cairn_south", "Relight the south scout cairn", tag="cairn_s",
                 location="location.frostfang.clan_hold", completion_flag="flag.beat.cairn_south_lit",
                 hint="The first cairn stands on the rise where the north road leaves the hold.",
                 journal="The south cairn burns small and white."),
        interact("interact.frostfang.scout_cairn_mid", "Relight the middle scout cairn in the pass", tag="cairn_m",
                 location="location.frostfang.glacier", completion_flag="flag.beat.cairn_mid_lit",
                 hint="North of the hold the road climbs into the pass. The second cairn is on the wind-scoured saddle.",
                 journal="A scout's mitten was frozen to the rim of the middle cairn. You left it where it was."),
        interact("interact.frostfang.scout_cairn_north", "Relight the north scout cairn on the glacier", tag="cairn_n",
                 location="location.frostfang.glacier", completion_flag="flag.beat.cairns_lit",
                 hint="The last cairn is on the blue ice at the head of the pass.",
                 journal="All three cairns burn. Far to the north-east, something blue answers them."),
        defend("location.frostfang.clan_hold", 60, "Hold the Clan Hold when the storm answers", tag="raid",
               req_flag="flag.beat.cairns_lit", completion_flag="flag.beat.hold_held",
               hint="Fall back to the moot ring and fight with the shield wall at your back. The motes are fast and fragile: do not chase them.",
               journal="The storm broke on the hold's shield wall. Hjalvar took a bolt through the shoulder and stayed on his feet."),
    ])

Q11 = Quest(
    id="quest.main.succession",
    title="Who the Clans Follow",
    summary="The storm took Hjalvar's shoulder, and the clans will not march on the Stormcrown behind a chief who cannot "
            "climb. Two men claim the right to lead them: the hold's own chief, and the exile who lost a hand to his "
            "judgement. Both want the one thing the clans cannot give themselves: a stranger's hand on the moot stone.",
    detail="The Stormbound's law is older than the hold: a chief who cannot lead into the storm names who will, or the "
           "clans do not go. Hjalvar would hold the passes and let the Tyrant's storm wear itself out on the wall. Halvar "
           "One-Hand would take a small party up the old winches at Stormfall and be at the Stormcrown before the next "
           "storm rises. Either way, someone has to put the ember on the stone.",
    chapter_key=CH2_FROST, order=2, region=FROST, level=9, giver_key=K("dlg.clan_chief.speaker"),
    auto_start="flag.main.closed_hold_done", completion_flag="flag.main.succession_done",
    sequential=True, xp=300, gold=100,
    objectives=[
        talk("dialogue.clan_chief", "Hear Hjalvar's claim", tag="hjalvar", location="location.frostfang.clan_hold",
             completion_flag="flag.beat.hjalvar_heard",
             hint="Hjalvar is on the bench by the moot ring, shoulder bound.",
             journal="Hjalvar would hold the passes and let the storm break itself on the wall."),
        talk("dialogue.clan_exile", "Hear Halvar One-Hand's claim", tag="halvar", location="location.frostfang.clan_hold",
             completion_flag="flag.beat.halvar_heard",
             hint="Halvar's fire is outside the hold's gate, a little too far away to be an accident.",
             journal="Halvar would take a small party up the old winches and be at the Stormcrown by morning."),
        talk("dialogue.frostfang_moot_stone", "Lay your hand on the moot stone", tag="stone",
             location="location.frostfang.clan_hold",
             hint="The moot stone stands in the middle of the moot ring. The eight clans are waiting.",
             journal="You stood at the stone with eight clans watching."),
        milestone("flag.beat.succession_decided", "Choose who the clans follow", tag="decide",
                  location="location.frostfang.clan_hold",
                  hint="Stand at the moot stone again if you walked away. Hjalvar holds; Halvar strikes.",
                  journal="The stone has been named. The clans moved."),
        defend("location.frostfang.stormcrown", 75, "Hold the Stormcrown road while the clans climb", tag="wall",
               req_flag="flag.fork.succession_hjalvar", completion_flag="flag.beat.stormcrown_held",
               hint="Stormfall is the track-head under the spire, far north-east of the hold past the glacier. Hold it while the clans climb.",
               journal="The clans climbed behind your shield wall and the storm did not break it."),
        interact("interact.frostfang.aerie_winch_a", "Crank the first winch at Stormfall", tag="winch_a",
                 location="location.frostfang.stormcrown", req_flag="flag.fork.succession_halvar",
                 hint="Two old winches stand at the head of the Stormfall track, far north-east of the hold past the glacier. A Syndicate crew oils them.",
                 journal="The first winch turned as if it had been waiting for you."),
        interact("interact.frostfang.aerie_winch_b", "Crank the second winch to the Stormcrown ledge", tag="winch_b",
                 location="location.frostfang.stormcrown", req_flag="flag.fork.succession_halvar",
                 completion_flag="flag.beat.winches_cranked",
                 hint="The second winch hauls the lift up the last stretch of cliff.",
                 journal="The lift shuddered up past old iron rings, and the spire was at arm's length."),
    ])

Q12 = Quest(
    id="quest.main.storm_tyrant",
    title="What the Storm Keeps",
    summary="The road to the Stormcrown is open and you stand under the spire. The Third Flamebearer comes down at the "
            "challenge brazier. When he falls, tell whoever holds the Stormbound hearth that the sky is quiet.",
    detail="The Storm Tyrant came home from the Celestial stair to find his clan hold burned by men who swore they were "
           "saving it, and he has been answering them with lightning for nearly four hundred years. The Stormbound call him "
           "the storm and do not say his name. The clans did not go to the Stormcrown when the weather began, and they "
           "will not go now, but they have stopped pretending he is only weather.",
    chapter_key=CH2_FROST, order=3, region=FROST, level=10, giver_key=K("dlg.clan_chief.speaker"),
    auto_start="flag.main.succession_done", completion_flag="flag.main.storm_tyrant_done",
    one_shot=True, sequential=True, xp=500, gold=160, reward_items=[("item.potion.health", 2)],
    objectives=[
        kill("enemy.storm_tyrant", 1, "Break the Storm Tyrant at the Stormcrown", tag="tyrant",
             location="location.frostfang.stormcrown",
             hint="Light the Lightning Rod brazier on the duelling ground. His motes gather early: use the storm pillars for cover.",
             journal="The Storm Tyrant fell, and for the first time in an age the Stormcrown was quiet."),
        milestone("flag.rival.duel1_won", "Answer the black-iron brazier at the Stormcrown", tag="duel", optional=True,
                  location="location.frostfang.stormcrown",
                  hint="A black-iron brazier stands cold near the Lightning Rod. The rider in black iron will answer it, and yields before he dies. Optional.",
                  journal="The rider in black iron yielded the field and said you would cross blades again."),
        talk("dialogue.clan_chief", "Tell Hjalvar the storm is done", tag="hjalvar", location="location.frostfang.clan_hold",
             req_flag="flag.fork.succession_hjalvar", hint="Hjalvar waits by the hearth in the Clan Hold.",
             journal="Hjalvar asked you to eat at his hearth as kin."),
        talk("dialogue.clan_exile", "Tell Halvar the storm is done", tag="halvar", location="location.frostfang.clan_hold",
             req_flag="flag.fork.succession_halvar", hint="Halvar sits on the moot stone, which no exile has touched in twenty years.",
             journal="The clans asked Halvar to take the high seat. He said he would think."),
    ])

# --- placed interactables ------------------------------------------------------------------------------------

CAIRN_S = clue(
    "scout_cairn_south", "The South Scout Cairn",
    "A cairn of flat grey stones with a stone lantern bowl on its crown, black and frozen. The oil in the bowl has set like "
    "candle fat. Storm char scars the windward face in a feathered pattern: something with a lot of lightning to spare has "
    "been here more than once. You break the ice with the flint haft, pour Sigrun's oil and strike. It catches on the "
    "fourth try, small, white and steady, and for the first time in a moon the hold has an eye on the south ridge.",
    "Leave it burning.", "The lantern burns small and steady. Somewhere below, a horn answers it.")

CAIRN_M = clue(
    "scout_cairn_mid", "The Middle Scout Cairn",
    "The middle cairn sits on a wind-scoured saddle where nothing has grown in a thousand years. A scout's mitten is frozen "
    "to the rim of the lantern bowl, and the scout who wore it did not come home to pull it free. The bowl is split from "
    "rim to base. You wedge it with the flint haft, pour Sigrun's oil into the crack and strike, and the flame goes up "
    "crooked and holds. The wind tries it twice and gives up.",
    "Leave the mitten where it is.", "The crooked flame burns on. The mitten is still frozen to the rim.")

CAIRN_N = clue(
    "scout_cairn_north", "The North Scout Cairn",
    "Ice has grown over the north cairn in layers, so the lantern bowl sits inside a block of blue glass with a flame-sized "
    "hollow at its heart. You chip down to it with the flint haft and pour the last of Sigrun's oil into the hollow. When "
    "it catches, the ice lights from within and throws a pale bar of fire across the glacier. Far away to the north-east, "
    "high on the spire you cannot see, something answers with a blue that is not a lantern. The hold's horn begins "
    "blowing behind you, far down the pass.",
    "Run for the hold.", "The ice glows from within. The hold's horn has stopped blowing.")

MOOT_STONE = Dialogue(
    id="dialogue.frostfang_moot_stone", speaker="The Moot Stone", start="root",
    start_variants=[StartVariant(has_flag("flag.beat.succession_decided"), "after")],
    nodes=[
        Node("root", "A waist-high stone worn smooth by eight clans' hands stands in the middle of the moot ring. Hjalvar has "
                     "been helped to the bench beside it, grey in the face, his shoulder bound. At the ring's far edge, "
                     "outside the firelight, Halvar One-Hand waits with his stump behind his back. The elders of the eight "
                     "clans are watching. They have all seen the ember in you. None of them will say it aloud.",
             [go("Lay your hand on the stone and name Hjalvar.", "hjalvar", tag="hjalvar",
                 do=(E.SET_FLAG, "flag.fork.succession_hjalvar"), do2=rep("faction.frostfang_clans", 15)),
              go("Lay your hand on the stone and name Halvar.", "halvar", tag="halvar",
                 do=(E.SET_FLAG, "flag.fork.succession_halvar"), do2=rep("faction.frostfang_clans", -15)),
              leave("Step back. Not yet.", tag="bye")]),
        Node("hjalvar", "'Hjalvar, then.' Your voice is not loud. The stone is warm under your palm. The elders shift, and "
                        "one by one they look at the chief instead of at you. Hjalvar does not smile. 'We go up as a wall. "
                        "Slowly, with the shamans in the middle and the young on the flanks. Three days of climbing, and it "
                        "will cost two of the old men their breath. It will not cost us the hold.' He looks across the ring "
                        "at the exile. 'You will not be judged twice, Halvar. Go home to your fire.' The exile bows to "
                        "nothing and goes.",
             [leave("Prepare to climb.", tag="go")]),
        Node("halvar", "'Halvar.' The stone is cold under your palm. The elders gasp as one and Hjalvar closes his eyes. "
                       "'So,' he says, quietly. 'The ember chooses the hand I took. I will not gainsay it. The clans will "
                       "not climb with me, but they will not stand in your way.' Halvar is already at your elbow, smiling "
                       "the smile of a man who has waited twenty years to be wrong in public. 'Winches at Stormfall. Two of "
                       "them, and a Syndicate crew has oiled them every year, so do not thank me too loudly. Three hours "
                       "up, and the Tyrant will still be eating breakfast.' He holds out a purse. 'Syndicate coin, for the "
                       "road. Take it or do not.'",
             [go("Take his coin.", "halvar_coin", tag="coin", do=give("item.currency.gold", 300),
                 do2=(E.ADD_CORRUPTION, "3")),
              leave("Refuse the coin and go.", tag="refuse")],
             on_enter=rep("faction.iron_syndicate", 10)),
        Node("halvar_coin", "The purse is heavy and warm from his hand. He watches you weigh it, and says nothing, which is "
                            "a kindness. You will remember that you took it.",
             [leave("Climb.", tag="go")]),
        Node("after", "The moot stone is cold. The ring is empty, and the clans have gone about the business of climbing.",
             [leave("Leave it be.", tag="bye")]),
    ])

WINCH_A = clue(
    "aerie_winch_a", "The First Winch",
    "An iron drum bigger than a man, its crank worn bright, the cable running up the cliff into cloud. Someone has "
    "greased it this year. You lean on the crank and it moves, grudgingly, then easily, and far above you a platform "
    "begins to descend through the mist with a long iron song.",
    "Lock the drum and move to the next.", "The drum is locked and the cable hums in the wind.")

WINCH_B = clue(
    "aerie_winch_b", "The Second Winch",
    "The second winch hauls the lift the last stretch of cliff to the Stormcrown ledge. You step onto the platform and "
    "work the crank until your arms shake. It shudders up past ice and old iron rings and a dead scout's marker, and "
    "somewhere above, thunder walks on the spire like a man pacing. When the platform bumps home, the duelling ground "
    "is thirty paces away and the brazier on its west side is the only light.",
    "Step off the platform.", "The lift rests at the top. The cable sings softly.")

DIALOGUES = [CAIRN_S, CAIRN_M, CAIRN_N, MOOT_STONE, WINCH_A, WINCH_B]

LOCALE = [
    ("ch.2.frostfang", "Frostfang Reach"),
    ("chapter.ch.2.frostfang.title", "Frostfang Reach"),
    ("chapter.ch.2.frostfang.subtitle", "The clans of Frostfang Reach have buried their dead twice and will not bury a third."),
    ("boss.storm_tyrant.epithet", "The Storm That Remembers"),
    ("boss.storm_tyrant.intro", "'Four hundred years I have waited for someone who was not afraid of the thunder.'"),
]


def build():
    return [Q10, Q11, Q12], DIALOGUES, LOCALE, "spec1"
