"""W-Spec-2, part 2: Act III, the truth (missions 23-25).

Mission 25 (quest.main.truth) and the Archivist's dialogue are the legacy objects edited in legacy.py;
their new text lives here as rows. Nothing in this module names the hidden realm: the Archivist speaks of
"the hidden country" and "the Concord" at most, and the pale.* variants are written in spec2_pale.py.
"""

from tools.campaign.model import (
    C, E, Dialogue, Node, Quest, StartVariant, defend, go, has_flag, interact, kill, leave, milestone,
    missing_flag, reach, say, set_flag, talk)
from tools.campaign.specs._spec2_common import back, bye, gated_clue, read_clue, t

CODE_FLAGS = {
    # data/story/rules/spec2.json: rule.spec2.seals_broken (all three plinth completion flags).
    "flag.beat.seals_broken": "story rule rule.spec2.seals_broken (data/story/rules/spec2.json)",
}

SUNSPIRE_LIBRARY = "location.sunspire.library"
STACKS = "location.sunspire.deep_stacks"      # new map location: see data/story/world_requirements_spec2.md

# --------------------------------------------------------------------------------------------------
# Mission 23: the five accounts
# --------------------------------------------------------------------------------------------------
SUNDERING_PAGES = Quest(
    id="quest.main.sundering_pages",
    title="The Five Accounts",
    summary="The fallen did not die as strangers. Each of them said one true thing at the end, and the five things together are a record of the Stair the gods never wrote down. Lay them before the Archivist, read them side by side, and take what is missing to the Archive's Annexe in Embermarket, where half of the key to the deep stacks is kept.",
    detail="Five accounts, five Flamebearers, one hole in the same place in each. The Archivist cannot read what comes next without the library's deep stacks, and the deep stacks are sealed with a sentence split between two keepers, one in the desert and one in the Ember Crown. Whoever has heard all five may ask for both halves.",
    chapter_key="ch.3", order=23, region="region.sunspire", level=22,
    giver_key="dlg.sunspire_archivist.speaker",
    auto_start="flag.main.hidden_done", completion_flag="flag.main.sundering_pages_done",
    sequential=True, xp=1100, gold=400,
    objectives=[
        milestone("flag.testimonies_all", "Carry the last words of all five fallen to the Archivist",
                  tag="gather",
                  hint="Each fallen Flamebearer speaks once more over their ember. If one slipped past you and cannot be found again, tell the Archivist so.",
                  journal="Five voices, five endings, and a hole of the same shape in each."),
        reach(SUNSPIRE_LIBRARY, "Carry the five accounts to the great library", tag="library",
              hint="The great library is in the Sunspire Dominion, at the end of the pilgrim road.",
              journal="The library was as I left it, except that the Archivist had cleared the long table."),
        talk("dialogue.sunspire_archivist", "Lay the five accounts before Archivist Seren Adaru", tag="archivist",
             location=SUNSPIRE_LIBRARY,
             hint="She is at the reading hall's long table.",
             journal="The Archivist read all five off my face and said every one of them was true, and every one had a hole."),
        interact("interact.sunspire.reading_table", "Read the five accounts side by side at the reading table",
                 tag="table", location=SUNSPIRE_LIBRARY, completion_flag="flag.beat.pages_read",
                 hint="The table stands in the middle of the reading hall. She has laid the accounts out for you.",
                 journal="Laid side by side the five accounts agree on a Stair, a throne, and a sixth Flamebearer who speaks in none of them."),
        reach("location.embermarket.annexe", "Take the missing page to the Archive Annexe in Embermarket", tag="annexe",
              hint="The Annexe is in the Ember Crown's great market. Waystones will bring you most of the way.",
              journal="The Annexe door was open and the Keeper stood in front of it with her hands behind her back, as though she had been told to expect me."),
        talk("dialogue.archive_keeper", "Ask Keeper Ysolde Marr for her half of the key", tag="keeper",
             location="location.embermarket.annexe",
             hint="She keeps the Annexe door. Tell her you have read the five accounts together.",
             journal="The Keeper said her half of the sentence once. I find that I cannot forget it."),
    ])

# --------------------------------------------------------------------------------------------------
# Mission 24: the vault (gated objectives, not sequential, so the three seals can be broken in any order)
# --------------------------------------------------------------------------------------------------
DEEP_STACKS = Quest(
    id="quest.main.deep_stacks",
    title="The Deep Stacks",
    summary="Behind the last shelf of the great library a stair descends to the vault where the gods' own record is kept. The Archive sealed it with a sentence and set wards on the door. What is written there is the reason six champions climbed to a throne and five of them did not come back.",
    detail="The wards are older than the library. They take trespass literally and they do not rest. Break them, break the three seal plinths that hold the codex shut, and hold the vault while the seals fall. Someone else has been waiting four hundred years to see the codex read, and he is not far behind you.",
    chapter_key="ch.3", order=24, region="region.sunspire", level=23,
    giver_key="dlg.sunspire_archivist.speaker",
    auto_start="flag.main.sundering_pages_done", completion_flag="flag.main.deep_stacks_done",
    xp=1300, gold=450, reward_items=[("item.potion.health", 3)],
    objectives=[
        reach(SUNSPIRE_LIBRARY, "Return to the great library", tag="library",
              hint="The stacks stair is behind the Archivist's last shelf.",
              journal="I went down past the last shelf, where the air changes."),
        interact("interact.sunspire.stacks_door", "Speak the sentence at the deep stacks door", tag="door",
                 location=SUNSPIRE_LIBRARY, completion_flag="flag.beat.stacks_open",
                 hint="The door is behind the last shelf. Say both halves of the Keeper's sentence to it.",
                 journal="The door took the sentence in two halves and opened without a sound."),
        kill("enemy.ward_golem", 3, "Break the ward golems that guard the vault", tag="golems",
             location=STACKS, req_flag="flag.beat.stacks_open", completion_flag="flag.beat.golems_down",
             hint="They wake when you cross the threshold. They are slow and they hit very hard: keep moving and strike when a swing has missed.",
             journal="The golems were set to keep out thieves, not to tell the invited from the uninvited. They did not try."),
        interact("interact.sunspire.seal_plinth_a", "Break the first seal plinth", tag="plinth_a", location=STACKS,
                 req_flag="flag.beat.golems_down", completion_flag="flag.beat.plinth_a",
                 hint="Three plinths stand in a triangle around the codex. Break them in any order.",
                 journal="The first seal let go with a sound like a held breath."),
        interact("interact.sunspire.seal_plinth_b", "Break the second seal plinth", tag="plinth_b", location=STACKS,
                 req_flag="flag.beat.golems_down", completion_flag="flag.beat.plinth_b",
                 hint="Each plinth takes a moment to answer. Do not walk away from it early.",
                 journal="The second seal cracked along its length. Somewhere behind the walls the wards stirred."),
        interact("interact.sunspire.seal_plinth_c", "Break the third seal plinth", tag="plinth_c", location=STACKS,
                 req_flag="flag.beat.golems_down", completion_flag="flag.beat.plinth_c",
                 hint="The last one gives only when you stop pushing it.",
                 journal="The third seal fell, and the vault filled with a light that was not any colour the library has."),
        defend(STACKS, 60, "Hold the vault while the seals fall", tag="hold", req_flag="flag.beat.seals_broken",
               completion_flag="flag.beat.library_defended",
               hint="The seals release what they held back. Stand in the vault, fight inside the ring of plinths, and do not leave it.",
               journal="The wards had one last thing in them: whatever the gods had set them to keep out. I held the vault until it spent itself."),
        talk("dialogue.rival_library", "Speak with the knight waiting among the broken seals", tag="rival",
             location=STACKS, req_flag="flag.beat.library_defended",
             hint="He stands at the vault's far end, beyond the plinths.",
             journal="The Ashen Knight was standing among the broken seals as though he had been there all along."),
        interact("interact.sunspire.sundering_codex", "Read the Sundering codex", tag="codex", location=STACKS,
                 req_flag="flag.beat.rival_parley_done", completion_flag="flag.beat.codex_read",
                 hint="The codex lies open on its lectern at the vault's heart.",
                 journal="The codex said what the Archivist had feared it would say. It also said what the Knight had been afraid I would read."),
    ])

# --------------------------------------------------------------------------------------------------
# Interactables
# --------------------------------------------------------------------------------------------------
READING_TABLE = read_clue(
    "dialogue.sunspire_reading_table", "The Reading Table",
    "Seren has laid the five accounts out the way she would lay out five witnesses, a hand's width apart, with a lamp burning at the head. You read them in the order you earned them: the Iron King's fear, the Tyrant's grief, the Beast's loneliness, the Prophet's certainty, the Queen's count.",
    then="Side by side they say the same thing in five voices. There was a Stair. Six climbed it. Five were thrown down at the top, not one of them fallen, and each of them heard a question that would not stop being asked. And in every account, in the same place, there is a gap where a sixth voice should be: the one who stayed.",
    then_button="Read them together.", close="Close the accounts.")

STACKS_DOOR = gated_clue(
    "dialogue.sunspire_stacks_door", "The Deep Stacks Door",
    "Behind the last shelf the wall is not a wall. It is a door the Archive stopped believing in: bare stone with a seam of lead down the middle, and no handle, no lock, nothing to turn. You say the Keeper's sentence to it, both halves, one after the other. The lead seam runs with light.",
    then="The door opens without a sound onto a stair that goes down a very long way. The air that comes up is dry and cold and smells of lamp oil. Somewhere below, stone shifts that has been still for a long time.",
    then_button="Say the sentence.", close="Go down.",
    gate_flag="flag.main.sundering_pages_done",
    dormant="Behind the last shelf the wall is not a wall: bare stone with a seam of lead down the middle, and no handle, no lock, nothing to turn. It does not answer. Whatever sentence opens it, you have not yet been given.")

PLINTH_A = gated_clue(
    "dialogue.sunspire_seal_a", "The First Plinth",
    "The first plinth is a block of black stone at hip height, cut with a ring of script that reads, in a hand you understand now, <<Not yet.>> When you lay your hand on the ring, the first seal on the codex cover lets go with a sound like a held breath.",
    close="Step back.", gate_flag="flag.beat.golems_down",
    dormant="A block of black stone, cut with a ring of script. The ring is cold. Whatever guards the vault is still awake, and the plinth will not answer while it stands.")
PLINTH_B = gated_clue(
    "dialogue.sunspire_seal_b", "The Second Plinth",
    "The second plinth reads <<Not you.>> It gives a little harder. Under your palm the ring warms, and a second seal on the codex cover cracks along its length. Somewhere behind the walls the wards stir.",
    close="Step back.", gate_flag="flag.beat.golems_down",
    dormant="A block of black stone, cut with a ring of script. The ring is cold. Whatever guards the vault is still awake, and the plinth will not answer while it stands.")
PLINTH_C = gated_clue(
    "dialogue.sunspire_seal_c", "The Third Plinth",
    "The third plinth reads <<Not ever.>> It does not give at all until you stop pushing and simply lean on it, the way you would on a tired friend. Then the third seal falls, and the vault floods with a light that is not any colour the library has.",
    close="Step back.", gate_flag="flag.beat.golems_down",
    dormant="A block of black stone, cut with a ring of script. The ring is cold. Whatever guards the vault is still awake, and the plinth will not answer while it stands.")


def _codex() -> Dialogue:
    return Dialogue(
        id="dialogue.sunspire_codex", speaker=t("The Sundering Codex"), start="root", nodes=[
            Node("root", t("The codex lies open on its lectern at the vault's heart, bound in something that was never an animal. The first leaf is blank. The second is a single line in a hand that is not human, repeated until it fills the page: <<WE KEPT IT. WE KEPT IT. WE KEPT IT.>> Turn the page, and the book will tell you the rest."), [
                say(t("Turn the page."), "read", tag="turn_first", when=missing_flag("flag.beat.codex_cards_seen"),
                    do=(E.PLAY_CARDS, "card.sundering"), do2=(E.SET_FLAG, "flag.beat.codex_cards_seen")),
                go(t("Turn the page."), "read", tag="turn", when=has_flag("flag.beat.codex_cards_seen")),
                leave(t("Close the codex."), tag="close")]),
            Node("read", t("The rest is the Sundering, told by the ones who were there, and it is not the story in the songs. Morthul did not rise against the gods. He kept the ending of things, and when the gods grew afraid of their own ending they asked him to hold it off for them, longer and longer, until holding was all he was. The war was not for the world. It was for the right to stop. The last leaf is in a rougher hand, a soldier's, written long after: <<Whoever sits is not a god. Whoever sits is a gate. The gate must not be asked to hold more than it can bear. The gate must not be empty. The gate must not be anyone who wants it.>>"), [
                go(t("Read it again."), "root", tag="again"),
                leave(t("Close the codex."), tag="close")]),
        ])


CODEX = _codex()


def _rival_library() -> Dialogue:
    choose = [
        go(t("Why would you stop me?"), "why", tag="why"),
        go(t("What is in the codex?"), "what", tag="what"),
        say(t("Read it with me."), "trust", tag="trust", when=missing_flag("flag.rival.library_trust"),
            when2=missing_flag("flag.rival.library_defy"), do=(E.SET_FLAG, "flag.rival.library_trust")),
        say(t("Stand aside. I will read it alone."), "defy", tag="defy", when=missing_flag("flag.rival.library_defy"),
            when2=missing_flag("flag.rival.library_trust"), do=(E.SET_FLAG, "flag.rival.library_defy")),
        leave(t("We will speak at the gate."), tag="later"),
    ]
    return Dialogue(
        id="dialogue.rival_library", speaker=t("The Ashen Knight"), start="root",
        start_variants=[
            StartVariant(has_flag("flag.rival.library_trust"), "after_trust"),
            StartVariant(has_flag("flag.rival.library_defy"), "after_defy"),
        ],
        nodes=[
            Node("root", t("He is standing among the broken seals with his helm under one arm, and he has been standing there a long time. It is the first time you have seen his face. It is a tired, ordinary face, older than it should be. <<Seventh. I came up through the floor of the world to stop you reading that book. I find I cannot say why. Four hundred years of knowing, and I have never once been sure.>>"),
                 choose, on_enter=(E.SET_FLAG, "flag.beat.rival_parley_done")),
            Node("why", t("<<Because everyone who reads it wants the throne. The fallen read it on the Stair, a page each, and each of them went down thinking the same thing: that it should be them. You will read it differently. Or you will not.>>"), [
                back("root", "Something else.", tag="back"), bye("We will speak at the gate.", tag="later")]),
            Node("what", t("<<The Sundering, and how it ended, which is not how the songs have it. And what the throne is for. There is a line near the end I have read four hundred times. Read it yourself, and then tell me I was wrong to be afraid.>>"), [
                back("root", "Something else.", tag="back"), bye("We will speak at the gate.", tag="later")]),
            Node("trust", t("He sets the helm down on a plinth, carefully, as if it might wake. <<Then read it aloud. I would like to hear what it sounds like, once.>> Something in his shoulders lowers. <<Take this for the Stair. Where I am going I will not need it.>> A flask is in your hand, and he is a few paces off again, as though he had not moved."), [
                bye("I will read it aloud.", tag="go")], on_enter=(E.GIVE_ITEM, "item.potion.health:3")),
            Node("defy", t("<<So be it.>> He steps back, and the helm goes on. <<You will find me at the gate, Seventh, and I will not be gentle. I was given a vigil, and I would rather keep it than be thanked.>>"), [
                bye("Then I will see you there.", tag="go")], on_enter=(E.ADD_CORRUPTION, "2")),
            Node("after_trust", t("He has not moved from where you left him, and the helm is still on the plinth. <<Read,>> he says gently. <<I will be here. I find I do not mind being here.>>"), [
                bye("I will.", tag="go")]),
            Node("after_defy", t("<<You have your answer,>> he says, from behind the helm. <<The book is open. Go and read it, and then go and make me regret it. At the gate.>>"), [
                bye("At the gate.", tag="go")]),
        ])


RIVAL_LIBRARY = _rival_library()

LOCALE = [
    ("chapter.ch.3.title", "Truth of the Gods"),
    ("chapter.ch.3.subtitle", "What the library kept, and what it was afraid to say"),
    # Story cards for the codex (PlayCards prefix card.sundering).
    ("card.sundering.1", t("Before the Sundering the gods kept the world between them: Light and Order, Life and Wisdom, Strength and Nature. And Morthul, who kept its endings, the one task none of the others would take.")),
    ("card.sundering.2", t("When the gods grew afraid of their own endings they asked Morthul to hold them off, a little longer, and then a little longer than that, until holding was all he was. The war that followed was fought for the right to stop.")),
    ("card.sundering.3", t("Six of the gods died in the Sundering. The world broke in the Cataclysm after it. And on a high terrace in heaven a seat remained that must never be empty, and began to make of whoever sat in it something that could bear it.")),
    # Mission 25 (quest.main.truth, legacy.py).
    ("quest.main.truth.detail", "Every fallen Flamebearer died saying the same thing in a different way, and now you have read why. The Archivist has kept the gods' own records for forty years without once saying them aloud. She will say them to someone who has read the codex, and then she will open the way."),
    ("quest.main.truth.hint_reading", "Ask her for the truth, all of it. She will tell it now that you have read the codex for yourself."),
    ("quest.main.truth.log_reading", "The Archivist was waiting with the lamp turned low. She wanted to hear the truth said aloud, since a thing read alone is only a rumour."),
    ("quest.main.truth.obj_open", "Open the way to the Celestial Realm"),
    ("quest.main.truth.hint_open", "At the end of her telling, ask her to open the way. The gate in the library forecourt will answer."),
    ("quest.main.truth.log_open", "She told it as the Archive tells it: a war fought for the right to stop, and a seat that must never be empty. The gate in the forecourt stands open."),
    # The Archivist's variants (dialogue.sunspire_archivist in legacy.py).
    ("dlg.archivist.reading", t("She is waiting at the head of the vault stair with the lamp turned low, and she does not ask what you read. She asks whether you are ready to hear it said aloud, since a thing read alone is only a rumour. <<I will tell it as the Archive tells it,>> she says, <<the way no one has told it in four hundred years. When I have finished, the way to the Celestial Realm will be open. It has always been open. It only wants someone who knows what is at the end of it.>>")),
    ("dlg.archivist.c_not_yet", "Not yet."),
    ("dlg.archivist.stacks_wait", t("<<You have Ysolde's half,>> she says, and gives you hers, once, so level and so quietly that you know you will never lose it. <<Say them one after the other at the door. I will not go down with you. I read the first leaf of that codex once, forty years ago, and it was enough. The wards are not guards: they are older than that, and they take a very literal view of trespass. Bring whatever you can fight with, and do not stay down once the seals fall.>>")),
    ("dlg.archivist.pages", t("She has cleared the long table before you reach it. <<Lay them down,>> she says, <<all five.>> You have nothing in your hands. You lay them down anyway: the Tyrant's grief, the Beast's loneliness, the Prophet's certainty, the Iron King's fear, the Queen's count. She reads them off your face. When she is finished she does not look up for a long time. <<Five accounts, each true, each with a hole of the same shape. Read them together at the reading table, and then take what is missing to the Archive's Annexe in Embermarket. Ysolde Marr keeps half a sentence that opens the deep stacks. I keep the other half. Neither of us may say it for the other.>>")),
    ("dlg.archivist.c_pages_fate", "What became of the hidden country?"),
    ("dlg.archivist.c_testimony_carry", "I cannot go back. Read me what I carry."),
    ("dlg.archivist.pages_released", t("<<My book on the Concord has a new last page this morning, in my own hand, and I did not write it. It says the count ended and the people began to age. It says some of them have already died, quietly, with their families, and some have taken up the lamplighter's trade for the first time in four hundred years. I will keep that page. It is the only one in the whole book that ends well.>>")),
    ("dlg.archivist.pages_kept", t("<<The book has a new last page. It says the count stood, and the Queen fell, and the years arrived in one night. It says half of a street sat down. I will not tell you it was wrong; I was not there. I will tell you I read it twice and then put the book where I could not see it.>>")),
    ("dlg.archivist.testimony_missing", t("<<Five voices go into the book, Seventh, and you have brought me fewer. The Iron King, the Storm Tyrant, the Beast Lord, the Prophet, the Queen: each of them said the one true thing they knew at the end, if you stayed to hear it. Go back to whoever you did not wait for. A fallen Flamebearer's last word is the only record of the Stair that was never written down. And if one of them died unheard, and no road leads back, tell me, and I will read what you carry and be sorry for the rest.>>")),
]


def build():
    return [SUNDERING_PAGES, DEEP_STACKS], [READING_TABLE, STACKS_DOOR, PLINTH_A, PLINTH_B, PLINTH_C, CODEX,
                                            RIVAL_LIBRARY], LOCALE, "spec2"
