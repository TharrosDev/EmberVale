"""W-Spec-1, Act II arc 3: the Sunspire Dominion (missions 16 to 18). See docs/playbook/campaign.md.

dry_wells -> prophets_flock (Fork F4: expose, turn, or kin at corruption 40+) -> crimson_prophet. Fork flags:
flag.fork.flock_exposed / flock_turned / flock_kin, written by dialogue.flock_deacon. flag.arc.sunspire_ready is written
by a story rule when prophets_flock is done and read by the Prophet's altar scene.
"""

from tools.campaign.model import (
    C, E, K, Dialogue, Node, Quest, StartVariant, corruption_at_least, go, has_flag, interact, kill, leave, milestone,
    reach, talk)
from tools.campaign.specs._s1 import CH2_SUN, SUN, clue, rep

CODE_FLAGS = {
    "flag.arc.sunspire_ready": "story rule rule.arc_sunspire (prophets_flock done), read by the Prophet's altar scene",
    "flag.beat.flock_decided": "story rules rule.flock_decided_*, data/story/rules/spec1.json",
    "flag.beat.prophetend_exposed": "story rule rule.prophetend_exposed (Prophet defeated + flock exposed), spec1.json",
    "flag.beat.prophetend_turned": "story rule rule.prophetend_turned (Prophet defeated + flock turned), spec1.json",
    "flag.beat.prophetend_kin": "story rule rule.prophetend_kin (Prophet defeated + flock kin), spec1.json",
    "flag.rival.duel2_won": "BossEncounterDirector, boss.ashen_knight_duel2 (the Knight yields)",
}

Q16 = Quest(
    id="quest.main.dry_wells",
    title="Dry Wells",
    summary="The oldest law in the Dominion is that the water at Saffra Wells is free. This season it tastes of ash. Cross "
            "the Caravan Gap, reach the wells, hear Mother Oda, read the three gauge stones, and ask the people the "
            "Prophet's pilgrims have left behind.",
    detail="Mother Oda Sarn has kept the wells for forty years and measured every season by three gauge stones the Archive "
           "set into the oasis wall. All three have gone grey. Her grandson walked south with the Prophet's people at "
           "midsummer, and the water has tasted worse every week since. Idrys Vane's caravans are turning back at the gap. "
           "Tamsin Reed walked the pilgrim track twice and will not do it a third time; she is the only one who will say why.",
    chapter_key=CH2_SUN, order=1, region=SUN, level=9, giver_key=K("dlg.sunspire_wellkeeper.speaker"),
    auto_start="flag.main.iron_king_done", completion_flag="flag.main.dry_wells_done",
    sequential=True, xp=250, gold=80, reward_items=[("item.potion.health", 2)],
    objectives=[
        reach("location.sunspire.caravan_gap", "Cross the Caravan Gap into the Dominion", tag="gap",
              completion_flag="flag.beat.gap_seen",
              hint="Take the caravan road south from the Crown to the Southmarch Gate and step through. A cairn on the far side marks the gap.",
              journal="A cairn marks the gap. Past it the road is scuffed bare by thousands of feet, every print pointing south."),
        reach("location.sunspire.wells", "Follow the caravan road south to Saffra Wells", tag="wells",
              hint="The road runs south across the dry basins. The oasis is the only green for a day's walk.",
              journal="Saffra Wells: a ring of green, a ring of carts, and not one caravan loading."),
        talk("dialogue.sunspire_wellkeeper", "Speak with Mother Oda at the wells", tag="oda", location="location.sunspire.wells",
             completion_flag="flag.beat.oda_briefed",
             hint="Mother Oda keeps the cistern at the centre of the oasis.",
             journal="Mother Oda poured a cup of her own water into the sand. She has never done that."),
        interact("interact.sunspire.well_gauge_east", "Read the east gauge stone", tag="gauge_e", location="location.sunspire.wells",
                 hint="Three gauge stones are set into the oasis wall: east, middle and west. Start with the east.",
                 journal="The east gauge is grey to the third line."),
        interact("interact.sunspire.well_gauge_mid", "Read the middle gauge stone", tag="gauge_m", location="location.sunspire.wells",
                 hint="The middle stone is above the cistern's overflow.",
                 journal="The middle gauge is grey to the fourth line."),
        interact("interact.sunspire.well_gauge_west", "Read the west gauge stone", tag="gauge_w", location="location.sunspire.wells",
                 completion_flag="flag.beat.gauges_read",
                 hint="The west stone is nearest the road north.",
                 journal="The west gauge is black to the top. Whatever fouls the water comes from upstream, and from the south."),
        talk("dialogue.sunspire_caravan_master", "Hear Idrys Vane out", tag="vane", location="location.sunspire.wells",
             hint="Vane is back at the wells with his turned-back carts.",
             journal="Vane has hired four swords this week. He never needed one in twenty years."),
        talk("dialogue.sunspire_pilgrim", "Ask Tamsin Reed what the jars are for", tag="tamsin", location="location.sunspire.wells",
             completion_flag="flag.beat.sacrament_known",
             hint="Tamsin hides behind the cistern's far wall. She walked the pilgrim track twice.",
             journal="The jars go south to be burned and come back as ash. The pilgrims tip it upstream and call it communion."),
    ])

Q17 = Quest(
    id="quest.main.prophets_flock",
    title="The Prophet's Flock",
    summary="The ash in the wells comes from the Crimson Mission, where the Prophet's flock pours the altar's soot upstream "
            "and calls it communion. Walk the pilgrim track to the chapel and decide what becomes of the people who drink it.",
    detail="The flock is not an army. It is two hundred people who were told that endings are a mercy, and some of them carry "
           "water to the altar twice a day because it is the only thing that quiets the voice. Deacon Haddan Voll keeps "
           "the chapel door. You can show them what the water does, you can speak to them in a voice they were waiting to "
           "hear, or, if you carry enough of the fire, you can tell them you already hear it too.",
    chapter_key=CH2_SUN, order=2, region=SUN, level=10, giver_key=K("dlg.sunspire_wellkeeper.speaker"),
    auto_start="flag.main.dry_wells_done", completion_flag="flag.main.prophets_flock_done",
    sequential=True, xp=300, gold=100,
    objectives=[
        reach("location.sunspire.mission", "Walk the pilgrim track to the Crimson Mission chapel", tag="chapel",
              hint="South from the wells past the red flats, keeping to the pilgrim track. Do not enter the sanctum yet.",
              journal="The chapel stands in front of the sanctum wall like a sleeve over a fist."),
        talk("dialogue.flock_deacon", "Speak with Deacon Haddan Voll at the chapel door", tag="deacon",
             location="location.sunspire.mission",
             hint="The deacon stands at the chapel door in red, with a ladle in one hand and a ledger of names in the other.",
             journal="The deacon asked if you had come to tell him the wells taste of ash."),
        milestone("flag.beat.flock_decided", "Decide what becomes of the flock", tag="decide",
                  location="location.sunspire.mission",
                  hint="Speak to the deacon again if you walked away. Expose the water, turn the flock, or (if the fire in you is strong enough) tell him you already hear it.",
                  journal="The flock has your answer."),
        kill("enemy.cultist", 4, "Drive off the zealots who will not hear it", tag="zealots",
             location="location.sunspire.mission", req_flag="flag.fork.flock_exposed",
             hint="Half the chapel walked out. The other half stayed on its knees, and some of them are armed.",
             journal="The zealots who stayed are down. The chapel door stands open on an empty nave."),
        talk("dialogue.sunspire_pilgrim", "Send Tamsin Reed to lead the leavers home", tag="tamsin",
             location="location.sunspire.wells", req_flag="flag.fork.flock_turned",
             hint="Tamsin waits at the wells with the pilgrims who left the chapel.",
             journal="Tamsin took two dozen of them to the cistern and taught them to fetch real water."),
        interact("interact.sunspire.sanctum_seal", "Take the Prophet's seal from the sanctum door", tag="seal",
                 location="location.sunspire.mission", req_flag="flag.fork.flock_kin",
                 hint="The deacon left it on the sanctum door's latch, on a red cord.",
                 journal="The door breathed warm air in your face, and a voice that knew your name sounded relieved."),
    ])

Q18 = Quest(
    id="quest.main.crimson_prophet",
    title="The Voice in the Altar",
    summary="The flock has been dealt with and the altar fire will take your flame. The Fifth Flamebearer is waiting in "
            "the sanctum with his sermon. Break him, and tell Mother Oda how the wells will taste now.",
    detail="He read in the great library for thirty years looking for a way to win that did not cost the world. Then a voice "
           "answered, patient and kind, and told him endings are a mercy. He does not think he betrayed anyone. He thinks he "
           "found the truth first. The wells were a sermon too: every cup a verse.",
    chapter_key=CH2_SUN, order=3, region=SUN, level=11, giver_key=K("dlg.sunspire_wellkeeper.speaker"),
    auto_start="flag.main.prophets_flock_done", completion_flag="flag.main.crimson_prophet_done",
    one_shot=True, sequential=True, xp=500, gold=160, reward_items=[("item.potion.health", 2)],
    objectives=[
        kill("enemy.crimson_prophet", 1, "Break the Crimson Prophet in his sanctum", tag="prophet",
             location="location.sunspire.mission",
             hint="Kindle the altar fire in the sanctum. His zealots fight in the nave, and they mean it.",
             journal="The Crimson Prophet fell, and his voice went on a while without him."),
        milestone("flag.rival.duel2_won", "Answer the black-iron brazier in the sanctum", tag="duel", optional=True,
                  location="location.sunspire.mission",
                  hint="A black-iron brazier stands cold at the sanctum's edge. The rider in black iron will answer it, and yields rather than dies. Optional.",
                  journal="The rider in black iron yielded a step at a time, smiling, and said he would not yield again."),
        talk("dialogue.sunspire_wellkeeper", "Tell Mother Oda the voice is silent", tag="oda", location="location.sunspire.wells",
             hint="Mother Oda is at the cistern in Saffra Wells.",
             journal="Mother Oda poured the first cup from a clean draw."),
    ])

# --- placed interactables ------------------------------------------------------------------------------------

GAUGE_E = clue(
    "well_gauge_east", "The East Gauge Stone",
    "A tablet of pale stone set into the oasis wall, scored with seven lines in the Archive's neat hand. Mother Oda has "
    "chalked her own readings beside each line for forty years: a long column of ones and twos. This season the water "
    "stain has risen to the third line and it is grey, not brown. Rub it and your thumb comes away silver-black. "
    "It is ash.",
    "Note the reading.", "The east gauge still reads grey to the third line.")

GAUGE_M = clue(
    "well_gauge_mid", "The Middle Gauge Stone",
    "The middle stone sits above the cistern's overflow, where the water is deepest. The stain here is grey to the fourth "
    "line. The chalk beside it is Mother Oda's, and for the last week the numbers have been written with a shaking hand. "
    "Someone has scratched under the gauge in a child's letters: THE WATER TALKS AT NIGHT. A man's hand has tried to "
    "scrub it out.",
    "Note the reading.", "The middle gauge reads grey to the fourth line. The scratched words are still under it.")

GAUGE_W = clue(
    "well_gauge_west", "The West Gauge Stone",
    "The west stone is nearest the road north and the first to take whatever comes down the channel. It has gone black "
    "to the top line. This is not seasonal silt: seven lines of the Archive's careful measure and the stain has gone "
    "past all of them. Whatever fouls the water comes from upstream, and the channel runs from the south, from the red "
    "flats and the pilgrim track.",
    "Note the reading.", "The west gauge is black to the top line. Nothing here has changed.")

DEACON = Dialogue(
    id="dialogue.flock_deacon", speaker="Deacon Haddan Voll", start="root",
    start_variants=[StartVariant(has_flag("flag.beat.flock_decided"), "after")],
    nodes=[
        Node("root", "Deacon Haddan Voll stands at the chapel door in red with a ladle in one hand and a ledger of names in "
                     "the other. He is perhaps forty and looks sixty. Behind him two hundred people kneel in rows facing a "
                     "wall of heat, and the whole nave smells of warm water and burnt sugar. 'You walked the pilgrim track,' "
                     "he says, kindly. 'Most of us did it on our knees. You have the look of someone who has seen the wells. "
                     "Have you come to tell me they taste of ash?'",
             [go("Show him the three readings from the gauge stones.", "expose", tag="expose",
                 when=has_flag("flag.beat.gauges_read"), do=(E.SET_FLAG, "flag.fork.flock_exposed"),
                 do2=rep("faction.veiled_archive", 8)),
              go("Lay the readings out as a reader of the Veiled Archive.", "expose_archive", tag="expose_archive",
                 when=has_flag("flag.beat.gauges_read"), when2=(C.GUILD_RANK_AT_LEAST, "faction.veiled_archive:0"),
                 do=(E.SET_FLAG, "flag.fork.flock_exposed"), do2=rep("faction.veiled_archive", 16)),
              go("Walk into the nave and speak to the flock.", "turned", tag="turn", do=(E.SET_FLAG, "flag.fork.flock_turned"),
                 do2=(E.ADD_CORRUPTION, "3")),
              go("Tell him you already hear it too.", "kin", tag="kin", when=corruption_at_least(40),
                 do=(E.SET_FLAG, "flag.fork.flock_kin"), do2=(E.ADD_CORRUPTION, "5")),
              go("Show the Emberbound sigil and tell him you already hear it too.", "kin_emberbound", tag="kin_emberbound",
                 when=corruption_at_least(40), when2=(C.GUILD_RANK_AT_LEAST, "faction.emberbound:0"),
                 do=(E.SET_FLAG, "flag.fork.flock_kin"), do2=rep("faction.emberbound", 12)),
              leave("Not yet.", tag="bye")]),
        Node("expose", "You do not shout. You set the three readings on his ledger one on top of another, grey on grey, and "
                       "tell him what is in the water he blesses: not the Prophet's voice, only ash from his own altar, "
                       "poured upstream twice a day by people he trusts. For a long moment the deacon looks at the "
                       "readings the way a man looks at a doctor's face. Then he turns to the nave and reads them aloud "
                       "in the same kind voice he uses for the names. The rows do not rise. They look at the ladle in his "
                       "hand. Someone starts to cry. Within the hour half the chapel is walking north with its jars "
                       "poured out on the red flats. The other half stays on its knees and does not look at you.",
             [leave("Step aside and let them pass.", tag="go")]),
        Node("expose_archive", "You set the three readings on his ledger, and under them the Archive's own gauge-tablet "
                               "seal, which he has seen on the oasis wall since boyhood. 'The Archive does not ask for belief,' "
                               "you tell him. 'It asks for the same measure, read twice, by two people who do not like each "
                               "other.' The deacon reads the numbers aloud in his kind voice. When he stops, no one in the nave "
                               "is looking at the wall of heat. Half the chapel is walking north with its jars poured out by "
                               "dusk, and a girl in the last row asks the nearest of you, shyly, what a gauge is. The other half "
                               "stays on its knees and does not look at you.",
             [leave("Step aside and let them pass.", tag="go")]),
        Node("kin_emberbound", "You show the sigil you were given at the Emberbound's table and say it quietly: 'I hear it "
                               "too.' The deacon's face opens like a door. 'The Brothers of the Flame sent one of theirs "
                               "to the Mission before,' he says. 'They did not stay. You will.' He kneels, and the nave "
                               "kneels with him. He lifts the chapel key from around his neck and puts it in your palm. "
                               "'The sanctum is open to you. He is waiting. He would like to speak before whatever comes "
                               "after.'",
             [leave("Take the key.", tag="go")]),
        Node("turned", "You walk past him into the nave and no one stops you. You stand where the heat is worst and say "
                       "nothing for a long time. Then you say their names back to them, the ones he has in his ledger, "
                       "not the ones the voice calls them. They look at the one who knows their names. 'The voice speaks "
                       "to everyone,' you tell them. 'It asked me too. I said no. You can do the same.' Some weep. Some "
                       "look at you the way they used to look at the wall of heat, and that frightens you more than the "
                       "weeping. Tamsin Reed, who followed you down the pilgrim track, begins to hand out water from a "
                       "cistern jar. Real water. The first cup is yours.",
             [leave("Drink, and step out of the heat.", tag="go")]),
        Node("kin", "'I hear it too,' you say, and the deacon's face opens like a door. 'Seventh of the Flame. He said you "
                    "would come. He said you would not need telling.' He kneels, and the whole nave kneels with him, two "
                    "hundred people moving like a field of wheat. He lifts the chapel key from around his neck and puts it "
                    "in your palm. 'The sanctum is open to you. He is waiting. He would like to speak before whatever comes "
                    "after.' Behind you, the heat in the wall rises to meet the heat in you.",
             [leave("Take the key.", tag="go")]),
        Node("after", "The deacon is not at the chapel door. The ladle hangs from a nail, and the nave is quiet in the way "
                      "of a room that has made up its mind.", [leave("Leave it be.", tag="bye")]),
    ])

SANCTUM_SEAL = clue(
    "sanctum_seal", "The Sanctum Door",
    "A disc of red wax pressed with the Prophet's flame hangs from the sanctum door's latch on a cord. The deacon has left "
    "it for you. When you take it the door breathes warm air in your face, and from deep inside comes a voice that knows "
    "your name and sounds, to your own shame, relieved.",
    "Pocket the seal.", "The latch is bare. The door is warm and does not breathe any more.",
    do2=(E.ADD_CORRUPTION, "2"))

GRANDSON = Dialogue(
    id="dialogue.oda_grandson", speaker="Rafe Sarn", start="root",
    start_variants=[StartVariant(has_flag("flag.fork.flock_turned"), "turned")],
    nodes=[
        Node("root", "Rafe Sarn sits on the stone lip of the cistern with his boots in the sand, thin, in a robe washed so "
                     "often it has gone from red to the colour of weak tea. 'I walked south at midsummer because it was "
                     "quiet there,' he says to the water. 'It is very quiet. I did not know quiet could be a hunger.' He "
                     "coughs into his sleeve. 'The Archive's readers let me hold the chalk this morning. A two, and a three. "
                     "I got the three wrong. Nobody told me off.'",
             [go("Do you still hear it?", "hear", tag="hear"), leave("Look after your grandmother.", tag="bye")]),
        Node("hear", "'A little. Like a tune from the next house.' He picks at the hem of the sleeve. 'Grandmother says it "
                     "fades if you give it something else to listen to. She sets me the gauges at dawn, and numbers are very "
                     "loud when you let them be.'",
             [go("Something else.", "root", tag="else"), leave("Keep reading them.", tag="bye")]),
        Node("turned", "Rafe Sarn stands among the grey cloaks at the cistern with a ladle in his hands, looking at the road "
                       "you came by. 'Tamsin says you told it no in front of everyone,' he says. 'I want to be able to do "
                       "that. I do not know how yet. Do you want a drink? It is only water. I check.'",
             [leave("Gladly.", tag="drink")]),
    ])

DIALOGUES = [GAUGE_E, GAUGE_M, GAUGE_W, DEACON, SANCTUM_SEAL, GRANDSON]

LOCALE = [
    ("ch.2.sunspire", "The Sunspire Dominion"),
    ("chapter.ch.2.sunspire.title", "The Sunspire Dominion"),
    ("chapter.ch.2.sunspire.subtitle", "The oldest law in the Dominion is that water is free. Someone is poisoning it."),
    ("boss.crimson_prophet.epithet", "The Voice in the Altar"),
    ("boss.crimson_prophet.intro", "'Come closer. It has been waiting to speak to you, and so have I.'"),
]


def build():
    return [Q16, Q17, Q18], DIALOGUES, LOCALE, "spec1"
