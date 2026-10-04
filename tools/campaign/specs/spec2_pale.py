"""W-Spec-2, part 1: the Pale Concord (missions 19-22, fork F5 the Queen's Question).

Every object here is a secret of the hidden realm, so every key is pale.* (SECRET = "pale"). Mission 22
(quest.main.hidden) is the legacy quest edited in legacy.py; its new text lives here as K() rows. The
hand-authored Undying dialogues are extended by _spec2_handedit.py, whose rows are listed below.
"""

from tools.campaign.model import (
    C, E, K, Dialogue, Node, Quest, StartVariant, corruption_at_least, defend, go, has_flag, interact, kill,
    leave, milestone, missing_flag, reach, say, set_flag, talk)
from tools.campaign.specs._spec2_common import back, bye, read_clue, t
from tools.campaign.specs import _spec2_handedit as HE

SECRET = "pale"

CODE_FLAGS = {
    # Fork F5 and the testimony the Queen's aftermath writes are consumed here; everything else is spec1's.
    # Hand-authored data: the OnEnter effect of the ember conversation's first node (_spec2_handedit.py ABSORB_TESTIMONY).
    "flag.testimony.iron": "dialogue.iron_king_absorb offer node (patched by _spec2_handedit.py)",
    "flag.testimony.storm": "dialogue.storm_tyrant_absorb offer node (patched by _spec2_handedit.py)",
    "flag.testimony.beast": "dialogue.beast_lord_absorb offer node (patched by _spec2_handedit.py)",
    "flag.testimony.prophet": "dialogue.crimson_prophet_absorb offer node (patched by _spec2_handedit.py)",
    "flag.testimonies_all": "StoryRuleDirector: all five flag.testimony.* set (W-Core-B code rule)",
}

TERMINAL_FLAGS = {}

# --------------------------------------------------------------------------------------------------
# Mission 19: the door
# --------------------------------------------------------------------------------------------------
PALE_DOOR = Quest(
    id="quest.main.pale_door",
    title="The Forecourt Door",
    summary="Three of the fallen are gone, and the embers you carry will not settle. They lean west, toward a country that is on no map. Archivist Seren Adaru says a door in her library's forecourt has begun to open. Walk through it and learn what four hundred years of silence were hiding.",
    detail="The Second Flamebearer hid her whole realm from death and from history, and the hiding held until her fellow fallen began to go out one by one. The Archivist has listened to the door breathe for forty years and never opened it. She will not stop you, and she will not follow.",
    chapter_key="ch.2.pale", order=19, region="region.pale_concord", level=20,
    giver_key="dlg.sunspire_archivist.speaker",
    auto_start="flag.pale_concord_revealed", completion_flag="flag.main.pale_door_done",
    sequential=True, xp=700, gold=250,
    objectives=[
        talk("dialogue.sunspire_archivist", "Speak with Archivist Seren Adaru in the great library", tag="archivist",
             location="location.sunspire.library",
             hint="She keeps the reading hall at the head of the library stair. The new door stands in the forecourt.",
             journal="The Archivist put down her book for the first time in my memory. A door that was bare wall for four hundred years had opened in the night."),
        reach("location.pale.landing", "Go through the door to the Still Quay", tag="quay",
              hint="The door is in the library forecourt. It opens only for someone carrying embers.",
              journal="The door let out onto a stone quay where every lamp burns at an hour that never ends."),
        interact("interact.pale.landing_lamp", "Touch the first lamp on the quay", tag="lamp",
                 location="location.pale.landing", completion_flag="flag.beat.pale_landing_lit",
                 hint="The first lamp stands a few steps from where you arrive, at the quay's south end.",
                 journal="The lamp was warm, and had not gone out in four hundred years. Something far off felt the touch."),
    ])

# --------------------------------------------------------------------------------------------------
# Mission 20: the investigation
# --------------------------------------------------------------------------------------------------
VESPERHOLD = Quest(
    id="quest.main.vesperhold",
    title="Vesperhold",
    summary="The Still Quay opens onto a city kept in perfect repair by people who no longer remember why. Its undying residents signed the Concord and have kept the same hour ever since. Somewhere in Vesperhold is the ledger the Hollow Queen counts at every dusk, and the reason she cannot stop counting.",
    detail="Four of Vesperhold's people will talk to a stranger, and each holds a piece of the same bargain. Hear them, find the registry the count is written in, and read the three old lamps whose flames keep the oldest memories. Whatever the Queen has become, she began as someone who could not bear to lose one more name.",
    chapter_key="ch.2.pale", order=20, region="region.pale_concord", level=20,
    giver_key="pale.dlg.steward.speaker",
    auto_start="flag.main.pale_door_done", completion_flag="flag.main.vesperhold_done",
    sequential=True, xp=900, gold=300, reward_items=[("item.potion.health", 2)],
    faction_reward=("faction.villagers", 10),
    objectives=[
        reach("location.pale.city", "Follow the quay road north to Vesperhold", tag="city",
              hint="The road runs from the Still Quay to the town gate. The Queen's servants do not walk inside the city.",
              journal="Vesperhold stood in perfect repair, every roof sound and every window clean, and not one door had been opened in a very long time."),
        talk("dialogue.undying_steward", "Speak with Steward Ismene Vael in the plaza", tag="steward",
             location="location.pale.city",
             hint="She stands near the well and reads the proclamation to anyone who stops.",
             journal="The Steward reads the Queen's proclamation to every visitor. She says it is still evening, and has been since the signing."),
        talk("dialogue.undying_lamplighter", "Speak with Oswin the Lamplighter", tag="lamplighter",
             location="location.pale.city",
             hint="He walks the lamps on the south side of the plaza.",
             journal="Oswin signed because he could not bear to watch his daughter grow old. She has not grown at all."),
        talk("dialogue.undying_baker", "Speak with Maren Loaf at her stall", tag="baker",
             location="location.pale.city",
             hint="The bakery stall stands on the north side of the plaza.",
             journal="Maren bakes bread nobody eats. She says most of the city stopped wanting the exemption long ago."),
        talk("dialogue.undying_clockkeeper", "Speak with Old Corwen at the clock tower", tag="clockkeeper",
             location="location.pale.city",
             hint="The clock tower stands at the plaza's west end.",
             journal="Corwen winds a clock that has said the same hour for four hundred years. He told me where the dusk-count is written."),
        interact("interact.pale.dusk_registry", "Read the dusk-count registry at the foot of the clock tower",
                 tag="registry", location="location.pale.city", completion_flag="flag.beat.registry_read",
                 hint="Corwen keeps it on a lectern beside the tower's door.",
                 journal="The registry holds eleven hundred and four names. The first signature is the Queen's, and it has been struck through."),
        interact("interact.pale.lamp_signing", "Touch the signing lamp at the plaza's south end", tag="lamp_signing",
                 location="location.pale.city", completion_flag="flag.beat.lamp_signing",
                 hint="It stands between the lamplighter's beat and the south road.",
                 journal="The first lamp remembered the morning of the signing: a whole kingdom's hands on one page."),
        interact("interact.pale.lamp_cradle", "Touch the cradle lamp by the east townhouse", tag="lamp_cradle",
                 location="location.pale.city", completion_flag="flag.beat.lamp_cradle",
                 hint="It stands at the corner of the east lane, near the well.",
                 journal="The second lamp remembered the last child born in Vesperhold, and the Queen running with her through the streets."),
        interact("interact.pale.lamp_queen", "Touch the Queen's lamp at the plaza's north edge", tag="lamp_queen",
                 location="location.pale.city", completion_flag="flag.beat.lamp_queen",
                 hint="It is the last lamp before the north street, the one that climbs toward the Court.",
                 journal="The third lamp remembered the Queen on the morning she signed her own name out. She was already counting."),
    ])

# --------------------------------------------------------------------------------------------------
# Mission 21: the count (fork F5)
# --------------------------------------------------------------------------------------------------
QUEENS_COUNT = Quest(
    id="quest.main.queens_count",
    title="The Queen's Count",
    summary="Every dusk the Hollow Queen counts her people, and the count is what holds the Concord together. Past Vesperhold the processional climbs to the Hollow Court, and at its foot a stone keeps the count. Whoever answers the stone decides whether the count goes on.",
    detail="The undying cannot stop the count and no longer want to. The stone will take an answer from anyone who carries an ember. Break the count and the years arrive gently, for every name. Keep it and the Concord holds until the Queen falls, and then it all comes due at once. Either answer opens the Court's gate. Neither is free.",
    chapter_key="ch.2.pale", order=21, region="region.pale_concord", level=21,
    giver_key="pale.dlg.steward.speaker",
    auto_start="flag.main.vesperhold_done",
    # The count-stone dialogue raises this flag; it is also this quest's completion flag, and quest.main.hidden
    # starts on it. The Hollow Queen's brazier is gated on it.
    completion_flag="flag.pale.court_open",
    # Not sequential: the husks stand in the road 12 to 28 m short of the stone, so a player fights them BEFORE
    # the Reach radius at the stone is entered; a locked Kill would never count them and could not be redone.
    xp=1000, gold=350,
    objectives=[
        reach("location.pale.court_approach", "Climb the processional to the Court approach", tag="approach",
              hint="The processional leaves Vesperhold by the north street and climbs to the Hollow Court.",
              journal="The processional climbed out of the preserved city into fields that never ripen and never rot."),
        kill("enemy.hollow_husk", 5, "Cut down the husks on the processional", tag="husks",
             location="location.pale.court_approach", completion_flag="flag.beat.husks_down",
             hint="The husks stand in the road, the ones the count could not hold. They come at anyone who nears the Court wall.",
             journal="The husks did not defend themselves. They had been standing in the road for four hundred years, waiting to be released from the road."),
        milestone("flag.beat.count_answered", "Answer the count-stone at the end of the processional", tag="stone",
                  location="location.pale.court_approach",
                  hint="Touch the stone and it will ask whether the count goes on. It stays silent while the husks stand. Either answer opens the Court's gate.",
                  journal="The stone asked, and I answered. The gate of the Hollow Court stands open."),
    ])


# --------------------------------------------------------------------------------------------------
# Interactables and the F5 stone
# --------------------------------------------------------------------------------------------------
LANDING_LAMP = read_clue(
    "dialogue.pale_landing_lamp", "The Quay Lamp",
    "The lamp is lit at an hour that never ends. The glass is warm. When you lay a hand on it the flame leans toward you, and then, slowly, away from you, toward the city.",
    then="A thin bell sounds, once, far off. Somewhere a door that has not moved in four hundred years moves. Back in the Archive's library an old lamp will have turned in its stand by now.",
    then_button="Hold the glass a moment longer.", close="Let it burn.")

REGISTRY = read_clue(
    "dialogue.pale_dusk_registry", "The Dusk-Count Registry",
    "The registry is chained to its lectern, a heavy book kept open at the last page. Eleven hundred and four names in eleven hundred and four hands, every one signed in the same black ink. At the head of the first page a signature has been struck through so hard the paper has torn.",
    then="The margin is in a single hand, small and level, written at dusk: <<Counted. Counted. Counted.>> It repeats down every page for four hundred years. Near the end the hand begins to shake. On the last line it has written only: <<Still counted. Is that enough?>>",
    then_button="Read the margin.", close="Close the book.")

LAMP_SIGNING = read_clue(
    "dialogue.pale_lamp_signing", "The Signing Lamp",
    "The flame stands up straight in its glass, the plaza goes quiet around you, and then it is morning. A table is carried into the square. A kingdom lines up to sign: smiths, midwives, the old, the very young held up to make a mark. Nobody weeps. They are afraid of the world outside, and the table is the only door it has left.",
    then="At the head of the table a tall woman in a plain coat reads the terms aloud, twice. When she reaches the clause that says none of them will die, her voice does not change. When she reaches the clause that says none will leave, her hand goes white on the page.",
    then_button="Look closer at the table.", close="Let the flame settle.")

LAMP_CRADLE = read_clue(
    "dialogue.pale_lamp_cradle", "The Cradle Lamp",
    "The flame leans toward the east lane, and you see it. A woman running with a bundle no older than nine days, while the bell rings the hour for signing. She does not run toward the table. She runs past it, and stops, and holds the child up to the sky one last time so that it will have seen the sky.",
    then="She is the Queen. The coat is the same. She carries the child to its father at the table and says, <<This one is last in the count. Keep her warm.>> The child was Oswin's daughter. She has not grown a day since. The Queen has known which house she sleeps in for four hundred years.",
    then_button="Follow the woman.", close="Let the flame settle.")

LAMP_QUEEN = read_clue(
    "dialogue.pale_lamp_queen", "The Queen's Lamp",
    "The third flame burns white, and the plaza empties of everyone but one. A tall woman in a plain coat stands at the registry with a pen, alone, at dusk. She writes her own name first, and then draws a line through it. Then she begins to count from the top.",
    then="A voice that is not quite hers and not quite the lamp's: <<A name in the count must be kept. A keeper cannot be a name in her own count, or who would be counting? She chose the count. She has chosen it every dusk since.>> The flame gutters. Somewhere north, a stone rings.",
    then_button="Ask why she crossed herself out.", close="Let the flame settle.")


def _count_stone() -> Dialogue:
    return Dialogue(
        id="dialogue.pale_count_stone", speaker=t("The Count-Stone"), start="root",
        start_variants=[StartVariant(has_flag("flag.beat.count_answered"), "settled"),
                        # The husks on the road are the Kill objective that gates the answer: until they are down
                        # the stone is silent (an answer given early would open the Queen's brazier over them).
                        StartVariant(missing_flag("flag.beat.husks_down"), "unready")],
        nodes=[
            Node("unready", t("A standing stone, black and wet-looking though nothing here ever rains, cut with ring upon ring of names. Under your hand it is cold and silent. Something is not finished on the road behind you, and the stone will not answer until it is."),
                 [leave(t("Step back."), tag="back")]),
            Node("root", t("A standing stone, black and wet-looking though nothing here ever rains, cut with ring upon ring of names. Under your hand it is cold, and then it speaks in a voice made of every dusk: <<The count is eleven hundred and four. Shall it go on?>>"), [
                go(t("What happens if it ends?"), "ends", tag="ask_end"),
                go(t("What happens if it goes on?"), "goes_on", tag="ask_on"),
                say(t("End the count."), "released", tag="release", when=missing_flag("flag.beat.count_answered"),
                    do=(E.SET_FLAG, "flag.fork.queen_released"), do2=(E.SET_FLAG, "flag.beat.count_answered")),
                say(t("Let the count go on."), "kept", tag="keep", when=missing_flag("flag.beat.count_answered"),
                    do=(E.SET_FLAG, "flag.fork.queen_kept"), do2=(E.SET_FLAG, "flag.beat.count_answered")),
                leave(t("Not yet."), tag="later"),
            ]),
            Node("ends", t("The stone answers plainly. <<Then the evening ends. Every name here is owed the years it was spared. They come due gently, if the count is broken first, and all at once if it is not. Some will be glad. Some will not live out the winter. The Queen will feel each one go.>>"), [
                back("root", "Ask again.", tag="back"), bye("Step back.", tag="bye")]),
            Node("goes_on", t("<<Then the count goes on. Nobody here dies, nobody leaves, nobody is born. The Queen keeps counting and her people keep her company. When she falls the bargain falls with her, and every year it held back arrives in a night. She will not remember what she counts for, long before that.>>"), [
                back("root", "Ask again.", tag="back"), bye("Step back.", tag="bye")]),
            Node("released", t("The rings on the stone go dark, one by one, from the outside in. Far south, in a city of perfect repair, a bell that has not rung in four hundred years rings once. It is a very small sound and it goes on a long time. Behind you the gate of the Hollow Court unbars itself."),
                 [leave(t("Go on."), tag="go", do=(E.ADD_REPUTATION, "faction.villagers:10"),
                        do2=(E.ADD_CORRUPTION, "-5"))],
                 on_enter=(E.PLAY_CARDS, "pale.card.count_release")),
            Node("kept", t("The rings on the stone brighten, ring after ring, until the black is shining. The count rolls on into the dusk as though it had never been asked. Far south, nothing changes at all, and every soul in Vesperhold feels it. Behind you the gate of the Hollow Court unbars itself."),
                 [leave(t("Go on."), tag="go", do=(E.ADD_REPUTATION, "faction.veiled_archive:10"),
                        do2=(E.ADD_CORRUPTION, "5"))],
                 on_enter=(E.PLAY_CARDS, "pale.card.count_keep")),
            Node("settled", t("The stone is quiet. Its rings have settled into whatever you chose, and it does not ask again."),
                 [leave(t("Leave it."), tag="leave")]),
        ])


COUNT_STONE = _count_stone()


def _queen_parley() -> Dialogue:
    return Dialogue(
        id="dialogue.hollow_queen_parley", speaker=t("The Hollow Queen"), start="root",
        start_variants=[
            StartVariant(has_flag("flag.hollow_queen_defeated"), "after"),
            StartVariant(has_flag("flag.beat.queen_truce"), "truce_after"),
            StartVariant(corruption_at_least(60), "truce"),
            StartVariant(has_flag("flag.fork.queen_released"), "released"),
            StartVariant(has_flag("flag.fork.queen_kept"), "kept"),
        ],
        nodes=[
            Node("root", t("The chair at the head of the forecourt is empty, and still a voice answers you from it, dry and level. <<You have come a long way to stand where nobody stands. State your business, Seventh, or light the brazier. I have not refused an audience in four hundred years.>>"), [
                go(t("What are you counting?"), "counting", tag="counting"),
                bye("I will light it.", tag="light")]),
            Node("counting", t("<<Everything that is still mine. It is not a long list any more. It was a kingdom once.>>"), [
                back("root", "Something else.", tag="back"), bye("I will light it.", tag="light")]),
            Node("released", t("<<You broke the count. I felt eleven hundred and four names leave my hands, one after another, like candles going out down a long hall. I have not been so empty since the day I signed. Do not expect me to thank you, and do not expect me to forgive you. Expect me at the brazier.>>"), [
                go(t("You held them against their will."), "released_why", tag="why"),
                bye("Then I will see you at the brazier.", tag="ring")]),
            Node("released_why", t("<<Against their will. Yes. By their own signatures, which I wrote the terms for. It is a very old argument and I lost it the day they stopped making it. Go on. Light it.>>"), [
                bye("I will.", tag="light")]),
            Node("kept", t("<<You let the count stand. You will think it was a kindness, and it was, a little. But I know what it is to be kept, Seventh. I tell myself each dusk that the next name will be the one I can bear to lose. Light the brazier. Take the choice from me, since you would not take it from them.>>"), [
                go(t("Would you have broken it yourself?"), "kept_why", tag="why"),
                bye("Then I will see you at the brazier.", tag="ring")]),
            Node("kept_why", t("<<Every dusk. Every dusk I stand at the stone with my hand on it, and every dusk I count instead.>> The voice is quiet for a while. <<Light it.>>"), [
                bye("I will.", tag="light")]),
            Node("truce", t("The voice is quiet a moment longer than courtesy allows. <<You smell of us. Of cold and keeping, and the kind of hunger that learns to call itself patience. How many of the fallen did you take in? Do not answer. I can hear them.>> The chair creaks as nothing sits in it. <<You are not here to end me. You are here to find out what you become. I will teach you the last craft I learned, and you may fight me for it afterwards, if you still want to. Sit with me a moment, Seventh. Nobody has in four hundred years.>>"), [
                say(t("Accept the craft."), "truce_taken", tag="accept",
                    do=(E.LEARN_SPELL, "spell.grave_mark"), do2=(E.ADD_CORRUPTION, "5")),
                go(t("Ask about the count you broke."), "released", tag="count_released",
                   when=has_flag("flag.fork.queen_released")),
                go(t("Ask about the count you kept."), "kept", tag="count_kept",
                   when=has_flag("flag.fork.queen_kept")),
                say(t("Refuse. Light the brazier."), "truce_refused", tag="refuse")]),
            Node("truce_after", t("The chair is as empty as before. <<A truce is only a pause, Seventh,>> says the dry voice. <<You have what I could give. Light the brazier, and let us finish it.>>"), [
                bye("I will.", tag="light")]),
            Node("truce_taken", t("The cold passes into your hands, patient and exact, and you know how to mark a thing so that it cannot hide from its ending. <<Good,>> she says, and sounds almost grateful. <<That is the whole of what I kept for myself. Now do it properly: light the brazier. A truce is only a pause.>>"), [
                bye("I will.", tag="light")], on_enter=(E.SET_FLAG, "flag.beat.queen_truce")),
            Node("truce_refused", t("<<Good,>> she says, and sounds almost proud. <<Then do it properly.>>"), [
                bye("I will.", tag="light")]),
            Node("after", t("The chair is still empty. The voice is gone from it. What is left is not a voice at all, only the shape of one: the count, carried on without her, one last time, until it reaches the end of the book. Then, far down the processional, the stone rings, and the shape says the thing it was waiting to say to someone who would carry it out. <<I was the Second. I did not fall from the Stair. I was thrown. Five of us were. The sixth climbed on. We heard him from below, a long time of armour on stone, and then nothing, a very long nothing, and that was the sound of the throne asking. Armour does not stop like that unless the man in it has knelt. Tell whoever asks that the Knight did not fall. He knelt.>>"), [
                go(t("What was the throne asking?"), "after_throne", tag="throne"),
                go(t("What happened to your people?"), "after_released", tag="fate_released",
                   when=has_flag("flag.fork.queen_released")),
                go(t("What happened to your people?"), "after_kept", tag="fate_kept",
                   when=has_flag("flag.fork.queen_kept")),
                go(t("You made the offer, before the fight."), "after_truce", tag="truce",
                   when=has_flag("flag.beat.queen_truce")),
                bye("I will carry it.", tag="carry")],
                 on_enter=(E.SET_FLAG, "flag.testimony.queen")),
            Node("after_throne", t("<<Whoever sits is the end of everything, or the end of the end. It is a very long chair. I never saw it. I think about it every dusk. Take what you carry to the Archive, Seventh. You have five voices now, and they were never meant to be heard one at a time.>>"), [
                back("after", "Tell me again.", tag="again"), bye("I will carry it.", tag="carry")]),
            Node("after_released", t("Far south a bell is ringing, a very small bell. <<They are growing old,>> says the shape. <<A grey hair. A knee that aches in the rain. I would have given a great deal to see it. I will have to settle for knowing.>>"), [
                back("after", "Tell me again.", tag="again"), bye("I will carry it.", tag="carry")]),
            Node("after_kept", t("<<The count stood, and I fell, and the years came due in one night,>> says the shape, without any reproach at all. <<It was a hard night. I am glad it was a night and not a century. Some of them chose it, at the end. Remember that, when you hear the other half of the story.>>"), [
                back("after", "Tell me again.", tag="again"), bye("I will carry it.", tag="carry")]),
            Node("after_truce", t("<<I offered you what I had, and you took it. I do not know if that was a mercy or a trap. I hope you never find out which.>>"), [
                back("after", "Tell me again.", tag="again"), bye("I will carry it.", tag="carry")]),
        ])


QUEEN_PARLEY = _queen_parley()

LOCALE = [
    ("pale.chapter.ch.2.pale.title", "The Pale Concord"),
    ("pale.chapter.ch.2.pale.subtitle", "A kingdom that signed away its endings"),
    # The Hollow Queen's brazier while the count-stone has not been answered (BossSummonComponent.LockedPromptKey).
    ("pale.boss.challenge_locked_count", "The brazier will not take a flame. The count at the foot of the processional has not been answered."),
    # Story cards (PlayCards prefixes) for the count-stone's answer.
    ("pale.card.count_release.1", t("The rings go dark from the outside in. The Queen feels each name leave her hands, and does not call any of them back.")),
    ("pale.card.count_release.2", t("In Vesperhold a lamplighter lifts his daughter onto his shoulder and notices, for the first time in four hundred years, that she is heavier than she was.")),
    ("pale.card.count_keep.1", t("The rings brighten. The count rolls on into the dusk as though it had never been asked, and eleven hundred and four people feel the evening settle back over them like a hand.")),
    ("pale.card.count_keep.2", t("In Vesperhold the Steward reads the proclamation a little louder. She does not look up, and she does not stop.")),
    # Mission 22 (quest.main.hidden, legacy.py): text for the appended objective and the new hints.
    ("pale.quest.hidden.summary", "The count-stone has answered, and the gate of the Hollow Court stands open. The Second Flamebearer waits at the end of the processional with eleven hundred and four names in her hands. End her reign, and hear what the Court remembers when she is gone."),
    ("pale.quest.hidden.detail", "The Hollow Queen bargained for her kingdom's lives and paid with her own soul, and the bargain has held for four hundred years. Light the brazier in the forecourt and she will give you an audience. If you carry enough of the fallen's fire she may offer you something first. When she is gone, her empty chair keeps one last account of the stair, and it was only ever meant for someone who would carry it out."),
    ("pale.quest.hidden.hint_queen", "Light the brazier in the Court's forecourt. A voice speaks from the empty chair beside it, and what it says depends on what you answered at the stone."),
    ("pale.quest.hidden.log_queen", "The Second Flamebearer is dead. For the first time in four hundred years the light moved."),
    ("pale.quest.hidden.obj_aftermath", "Hear what the Court remembers at the Queen's empty chair"),
    ("pale.quest.hidden.hint_aftermath", "The chair stands beside the brazier in the forecourt. It still has something to say."),
    ("pale.quest.hidden.log_aftermath", "The chair kept the Queen's last account: that the Six climbed the Stair, five were thrown down, and the sixth knelt at the top and did not fall."),
    # The Archivist's pale variants (dialogue.sunspire_archivist in legacy.py).
    ("pale.dlg.archivist.door", t("She has put the book down, and that alone is new. <<It opened in the night. The door in the forecourt, the one that was bare wall for four hundred years. I have listened to it breathe for forty. Last night it breathed out.>> Her hands are not quite steady on the table. <<There is a country on the other side. I have a thousand names for it and none I trust. The fire you carry leans toward that door. I do not need to tell you why.>>")),
    ("pale.dlg.archivist.c_door_what", "What is on the other side?"),
    ("pale.dlg.archivist.door_what", t("<<The Concord. That is what the oldest of my books call it, when they call it anything: a signed arrangement between a dying world and a woman with a very good pen. Nobody who went in came back to say whether it worked. The books stop at the signing. Whoever kept that secret kept it better than anyone has kept anything.>>")),
    ("pale.dlg.archivist.c_door_why", "Why has it opened now?"),
    ("pale.dlg.archivist.door_why", t("<<Because three of the fallen are dead, and she is the Second. A seal like that is held up by a set of keys, and you have been taking the keys one by one. You did not know. I did, and I let you. I am sorry for that, and not sorry enough to have stopped you.>>")),
    ("pale.dlg.archivist.c_door_go", "I will go and see."),
    ("pale.dlg.archivist.lamp_turned", t("The old lamp on her desk has turned to face the forecourt door on its own. She is watching it the way she watched the book. <<You touched something over there,>> she says. <<At that moment this lamp turned. I have trimmed it for forty years and it has never once moved. Whatever is on the other side knows you are coming. Mind the Steward: she talks, and she means what she says.>>")),
] + HE.ROWS


def build():
    return [PALE_DOOR, VESPERHOLD, QUEENS_COUNT], [LANDING_LAMP, REGISTRY, LAMP_SIGNING, LAMP_CRADLE, LAMP_QUEEN,
                                                    COUNT_STONE, QUEEN_PARLEY], LOCALE, "spec2"
