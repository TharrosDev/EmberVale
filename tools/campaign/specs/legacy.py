"""The main story as the finish run (2026-09-27) shipped it: four quests and two conversations.

Reproduces what tools/gen_main_story.py wrote, so running the generator with only this spec leaves
those six files unchanged apart from the header line. Their locale rows are hand-authored in
strings.csv already, so every text here is a K(...) key and this spec emits no locale rows.
Node and choice ids are the ones the tests and HeadlessStory depend on: do not rename them.
"""

from tools.campaign.model import (C, E, K, Dialogue, Node, Quest, kill, say, talk)


def _quest(slug, auto, objectives, sequential=False, ledger=False):
    qid = f"quest.main.{slug}"
    return Quest(
        id=qid, title=K(f"{qid}.title"), summary=K(f"{qid}.summary"), objectives=objectives,
        xp=600, gold=200, sequential=sequential, auto_start=auto, one_shot=True, ledger=ledger)


def _kill(target, location, desc):
    return kill(target, 1, K(desc), location=location)


# quest.main.gathering is the hidden "ledger" umbrella of Act II (docs/playbook/campaign.md): the three
# arcs' quests do the real work, this one only records the three Flamebearer kills.
QUESTS = [
    _quest("gathering", "flag.iron_king_defeated", ledger=True, objectives=[
        _kill("enemy.storm_tyrant", "location.frostfang.stormcrown", "quest.main.gathering.obj_storm"),
        _kill("enemy.beast_lord", "location.ashen.beast_lair", "quest.main.gathering.obj_beast"),
        _kill("enemy.crimson_prophet", "location.sunspire.mission", "quest.main.gathering.obj_prophet"),
    ]),
    _quest("hidden", "flag.pale_concord_revealed", [
        _kill("enemy.hollow_queen", "location.pale.palace", "quest.main.hidden.obj"),
    ]),
    _quest("celestial", "flag.celestial_gate_open", [
        _kill("enemy.ashen_knight", "location.celestial.knight_gate", "quest.main.celestial.obj_knight"),
        _kill("enemy.morthul", "location.celestial.ash_throne", "quest.main.celestial.obj_morthul"),
    ], sequential=True),
    # Act III is a conversation, not a kill: a Talk objective on the Archivist's dialogue.
    _quest("truth", "flag.hollow_queen_defeated", [
        talk("dialogue.sunspire_archivist", K("quest.main.truth.obj"),
             location="location.sunspire.library", sub_id="Obj_talk"),
    ]),
]


def _n(nid, text_key, *choices):
    return Node(nid, K(text_key), list(choices))


def _c(key, goto="", cond=C.ALWAYS, carg="", eff=E.NONE, earg="", **kw):
    return say(K(key), goto, when=(cond, carg), do=(eff, earg), **kw)


def _mg(key, goto="", eff=E.NONE, earg="", **kw):
    """The magic-teacher additions: hand-written in the compact style, defined ahead of their use."""
    return _c(key, goto, eff=eff, earg=earg, compact=True, early=True, **kw)


# The Archivist is Sunspire's (Seren Adaru); her library and Prophet topics are kept and the
# Act II/III story branches hang off the same root.
def _early(node):
    node.early = True
    return node


ARCHIVIST = Dialogue(
    id="dialogue.sunspire_archivist", speaker=K("dlg.sunspire_archivist.speaker"), start="root", nodes=[
        _n("root", "dlg.sunspire_archivist.root",
           _c("dlg.archivist.c_truth", "cataclysm", C.HAS_FLAG, "flag.hollow_queen_defeated"),
           _c("dlg.archivist.c_gate", "gate", C.HAS_FLAG, "flag.celestial_gate_open"),
           _c("dlg.archivist.c_hidden", "hidden", C.MISSING_FLAG, "flag.hollow_queen_defeated"),
           _c("dlg.sunspire_archivist.c_library", "library"),
           _c("dlg.sunspire_archivist.c_prophet", "prophet", C.MISSING_FLAG, "flag.crimson_prophet_defeated"),
           _mg("spell.teach.ask_menu", "mg_teach", sub_id="ch_mg_menu"),
           _c("dlg.sunspire_archivist.c_bye", sub_id="ch_root_5")),
        _n("library", "dlg.sunspire_archivist.library", _c("dlg.sunspire_archivist.c_library_bye")),
        _n("prophet", "dlg.sunspire_archivist.prophet", _c("dlg.sunspire_archivist.c_prophet_bye")),
        _n("hidden", "dlg.archivist.hidden", _c("dlg.archivist.c_back", "root")),
        _n("cataclysm", "dlg.archivist.cataclysm", _c("dlg.archivist.c_more", "morthul")),
        _n("morthul", "dlg.archivist.morthul", _c("dlg.archivist.c_more", "throne")),
        _n("throne", "dlg.archivist.throne",
           _c("dlg.archivist.c_open", "opened", C.MISSING_FLAG, "flag.celestial_gate_open",
              E.SET_FLAG, "flag.celestial_gate_open"),
           _c("dlg.archivist.c_back", "root")),
        _n("opened", "dlg.archivist.opened", _c("dlg.sunspire_archivist.c_bye")),
        _n("gate", "dlg.archivist.gate", _c("dlg.archivist.c_back", "root")),
        # Magic upgrade: she teaches Blink.
        _early(_n("mg_teach", "spell.teach.sunspire_archivist",
                  _mg("spell.blink.ask", "mg_taught", E.LEARN_SPELL, "spell.blink", sub_id="ch_mg_blink"),
                  _mg("spell.teach.back", "root", sub_id="ch_mg_back"))),
        _early(_n("mg_taught", "spell.teach.sunspire_archivist.taught",
                  _mg("spell.teach.thanks", "root", sub_id="ch_mg_thanks"))),
    ],
    comment="Magic upgrade: this NPC teaches spellcraft (spell.teach.* strings, LearnSpell effect).")

# The ending choice. Corruption decides which doors are open: under 40 only Dawnfire, 60 and over
# only the throne, and the band between may choose (CorruptionTiers). Both paths confirm first.
THRONE = Dialogue(
    id="dialogue.ash_throne", speaker=K("dlg.throne.speaker"), start="gate", nodes=[
        # The throne is also a placed talkable (celestial/ash_throne.tscn), so the choice can be
        # re-opened by a player who walked away; this first node holds it shut while Morthul still sits.
        _n("gate", "dlg.throne.gate",
           _c("dlg.throne.c_approach", "throne", C.HAS_FLAG, "flag.morthul_defeated"),
           _c("dlg.throne.c_leave", "", C.MISSING_FLAG, "flag.morthul_defeated")),
        _n("throne", "dlg.throne.intro",
           _c("dlg.throne.c_refuse", "dawn", C.CORRUPTION_BELOW, "60"),
           _c("dlg.throne.c_sit", "embers", C.CORRUPTION_AT_LEAST, "40"),
           _c("dlg.throne.c_later")),
        _n("dawn", "dlg.throne.dawn",
           _c("dlg.throne.c_dawn_yes", "", C.MISSING_FLAG, "flag.ending_embers", E.SET_FLAG, "flag.ending_dawnfire"),
           _c("dlg.throne.c_wait", "throne")),
        _n("embers", "dlg.throne.embers",
           _c("dlg.throne.c_embers_yes", "", C.MISSING_FLAG, "flag.ending_dawnfire", E.SET_FLAG, "flag.ending_embers"),
           _c("dlg.throne.c_wait", "throne")),
    ])


def build():
    return QUESTS, [ARCHIVIST, THRONE], [], "legacy"
