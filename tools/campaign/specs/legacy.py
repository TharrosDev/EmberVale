"""The main story as the finish run (2026-09-27) shipped it: four quests and two conversations, edited by the
campaign overhaul.

The original objective rows and the dialogue texts are hand-authored in strings.csv already, so every text
here is a K(...) key. W-Spec-2 (tag spec2) extended three quests and both dialogues APPEND-ONLY: new
objectives go at the end, existing objective order and sub-resource ids never change, and the new text
lives as K() rows in the spec2 block (spec2_pale.py / spec2_act3.py / spec2_act4.py). W-Spec-1 owns
quest.main.gathering (the Act II ledger).
Node and choice ids are the ones the tests and HeadlessStory depend on: do not rename them.
"""

from tools.campaign.model import (C, E, K, Dialogue, Node, Quest, StartVariant, kill, milestone, say, talk)


def _quest(slug, auto, objectives, sequential=False, ledger=False, **fields):
    """fields: the overhaul's additions (completion_flag, chapter_key, order, region, level, giver_key,
    detail, summary). `summary` replaces the original row when given."""
    qid = f"quest.main.{slug}"
    summary = fields.pop("summary", None) or K(f"{qid}.summary")
    return Quest(
        id=qid, title=K(f"{qid}.title"), summary=summary, objectives=objectives,
        xp=600, gold=200, sequential=sequential, auto_start=auto, one_shot=True, ledger=ledger, **fields)


def _kill(target, location, desc, **kw):
    return kill(target, 1, K(desc), location=location, **kw)


# quest.main.gathering is the hidden "ledger" umbrella of Act II (docs/playbook/campaign.md): the three
# arcs' quests do the real work, this one only records the three Flamebearer kills.
QUESTS = [
    _quest("gathering", "flag.iron_king_defeated", ledger=True, objectives=[
        _kill("enemy.storm_tyrant", "location.frostfang.stormcrown", "quest.main.gathering.obj_storm"),
        _kill("enemy.beast_lord", "location.ashen.beast_lair", "quest.main.gathering.obj_beast"),
        _kill("enemy.crimson_prophet", "location.sunspire.mission", "quest.main.gathering.obj_prophet"),
    ]),
    # Mission 22. Starts when the count-stone has answered (flag.pale.court_open, the Hollow Queen's
    # brazier gate). Kill first, then the aftermath conversation at her empty chair.
    _quest("hidden", "flag.pale.court_open", [
        _kill("enemy.hollow_queen", "location.pale.palace", "quest.main.hidden.obj",
              hint=K("pale.quest.hidden.hint_queen"), journal=K("pale.quest.hidden.log_queen")),
        talk("dialogue.hollow_queen_parley", K("pale.quest.hidden.obj_aftermath"), location="location.pale.palace",
             hint=K("pale.quest.hidden.hint_aftermath"), journal=K("pale.quest.hidden.log_aftermath")),
    ], sequential=True, completion_flag="flag.main.hidden_done", chapter_key="ch.2.pale", order=22,
        region="region.pale_concord", level=22, giver_key="pale.dlg.hollow_queen_parley.speaker",
        summary=K("pale.quest.hidden.summary"), detail=K("pale.quest.hidden.detail")),
    # Mission 29. Starts when the Stair is lowered; the Knight's brazier is gated on the vigil flag, which
    # only the kneeling Knight (visible from this quest's own start flag) can raise.
    _quest("celestial", "flag.main.sundered_stair_done", [
        _kill("enemy.ashen_knight", "location.celestial.knight_gate", "quest.main.celestial.obj_knight",
              hint=K("quest.main.celestial.hint_knight"), journal=K("quest.main.celestial.log_knight")),
        _kill("enemy.morthul", "location.celestial.ash_throne", "quest.main.celestial.obj_morthul",
              hint=K("quest.main.celestial.hint_morthul"), journal=K("quest.main.celestial.log_morthul")),
    ], sequential=True, completion_flag="flag.main.celestial_done", chapter_key="ch.4", order=29,
        region="region.celestial", level=28, giver_key="dlg.rival_gate.speaker",
        detail=K("quest.main.celestial.detail")),
    # Mission 25. The Talk objective used to be one skippable chat; it now needs the codex read first
    # (flag.beat.codex_read, mission 24), and the quest also needs the way opened (Milestone), so neither a
    # stray conversation nor an early reading can finish it.
    _quest("truth", "flag.main.deep_stacks_done", [
        talk("dialogue.sunspire_archivist", K("quest.main.truth.obj"),
             location="location.sunspire.library", sub_id="Obj_talk", req_flag="flag.beat.codex_read",
             hint=K("quest.main.truth.hint_reading"), journal=K("quest.main.truth.log_reading")),
        milestone("flag.celestial_gate_open", K("quest.main.truth.obj_open"), tag="open",
                  location="location.sunspire.library", hint=K("quest.main.truth.hint_open"),
                  journal=K("quest.main.truth.log_open")),
    ], sequential=True, completion_flag="flag.main.truth_done", chapter_key="ch.3", order=25,
        region="region.sunspire", level=24, giver_key="dlg.sunspire_archivist.speaker",
        detail=K("quest.main.truth.detail")),
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


def _v(flag, node):
    return StartVariant((C.HAS_FLAG, flag), node)


# Campaign overhaul: the Archivist's opening depends on how far the story has got. The variants are tried
# top-down, so the LATEST beat is listed first; "root" is the ordinary hub. Nodes door..testimony_missing
# are appended at the end; their rows are in spec2_pale.py (pale.dlg.archivist.*) and spec2_act3.py.
ARCHIVIST_VARIANTS = [
    _v("flag.celestial_gate_open", "root"),          # the way is open: the hub (its c_gate line)
    _v("flag.beat.codex_read", "reading"),           # mission 25: the codex is read, she tells it aloud
    _v("flag.beat.stacks_key_given", "stacks_wait"),  # mission 24: the Keeper has given her half
    _v("flag.testimonies_all", "pages"),             # mission 23: the five accounts
    _v("flag.hollow_queen_defeated", "testimony_missing"),   # the Queen is dead but an account is missing
    _v("flag.beat.pale_landing_lit", "lamp_turned"),  # mission 19: the old lamp turned
    _v("flag.pale_concord_revealed", "door"),        # mission 19: the door has opened
]

ARCHIVIST = Dialogue(
    id="dialogue.sunspire_archivist", speaker=K("dlg.sunspire_archivist.speaker"), start="root", nodes=[
        _n("root", "dlg.sunspire_archivist.root",
           # The truth is told only after the codex is read (mission 24), not merely after the Queen's death.
           _c("dlg.archivist.c_truth", "cataclysm", C.HAS_FLAG, "flag.beat.codex_read"),
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
           # The reading sets the truth flag the quest needs and opens the way (flag.celestial_gate_open).
           _c("dlg.archivist.c_open", "opened", C.MISSING_FLAG, "flag.celestial_gate_open",
              E.SET_FLAG, "flag.celestial_gate_open", do2=(E.SET_FLAG, "flag.truth.reading_done")),
           _c("dlg.archivist.c_back", "root")),
        _n("opened", "dlg.archivist.opened", _c("dlg.sunspire_archivist.c_bye")),
        _n("gate", "dlg.archivist.gate", _c("dlg.archivist.c_back", "root")),
        # Magic upgrade: she teaches Blink.
        _early(_n("mg_teach", "spell.teach.sunspire_archivist",
                  _mg("spell.blink.ask", "mg_taught", E.LEARN_SPELL, "spell.blink", sub_id="ch_mg_blink"),
                  _mg("spell.teach.back", "root", sub_id="ch_mg_back"))),
        _early(_n("mg_taught", "spell.teach.sunspire_archivist.taught",
                  _mg("spell.teach.thanks", "root", sub_id="ch_mg_thanks"))),
        # --- campaign overhaul (W-Spec-2), appended ---
        _n("door", "pale.dlg.archivist.door",
           _c("pale.dlg.archivist.c_door_what", "door_what"),
           _c("pale.dlg.archivist.c_door_why", "door_why"),
           _c("pale.dlg.archivist.c_door_go"),
           _c("dlg.archivist.c_back", "root")),
        _n("door_what", "pale.dlg.archivist.door_what",
           _c("dlg.archivist.c_back", "door"), _c("pale.dlg.archivist.c_door_go")),
        _n("door_why", "pale.dlg.archivist.door_why",
           _c("dlg.archivist.c_back", "door"), _c("pale.dlg.archivist.c_door_go")),
        _n("lamp_turned", "pale.dlg.archivist.lamp_turned",
           _c("dlg.archivist.c_back", "root"), _c("pale.dlg.archivist.c_door_go")),
        # The escape hatch: a save that predates the testimonies (or a voice that died unheard) can still go on.
        _n("testimony_missing", "dlg.archivist.testimony_missing",
           _c("dlg.archivist.c_testimony_carry", "pages", eff=E.SET_FLAG, earg="flag.testimonies_all"),
           _c("dlg.archivist.c_back", "root"), _c("dlg.sunspire_archivist.c_bye")),
        # The five accounts are laid before her (mission 23's Talk objective); the node records it on entry.
        Node("pages", K("dlg.archivist.pages"), [
            _c("dlg.archivist.c_pages_fate", "pages_released", C.HAS_FLAG, "flag.fork.queen_released"),
            _c("dlg.archivist.c_pages_fate", "pages_kept", C.HAS_FLAG, "flag.fork.queen_kept"),
            _c("dlg.archivist.c_back", "root"), _c("dlg.sunspire_archivist.c_bye")],
             on_enter=(E.SET_FLAG, "flag.beat.pages_laid")),
        _n("pages_released", "pale.dlg.archivist.pages_released", _c("dlg.archivist.c_back", "pages")),
        _n("pages_kept", "pale.dlg.archivist.pages_kept", _c("dlg.archivist.c_back", "pages")),
        # Her half of the Keeper's sentence is given on entry; the stacks door answers only once it is held.
        Node("stacks_wait", K("dlg.archivist.stacks_wait"), [
            _c("dlg.archivist.c_back", "root"), _c("dlg.sunspire_archivist.c_bye")],
             on_enter=(E.SET_FLAG, "flag.beat.stacks_half_given")),
        _n("reading", "dlg.archivist.reading",
           _c("dlg.archivist.c_truth", "cataclysm"),
           _c("dlg.archivist.c_not_yet", "root")),
    ],
    start_variants=ARCHIVIST_VARIANTS,
    comment="Magic upgrade: this NPC teaches spellcraft (spell.teach.* strings, LearnSpell effect).")

# The ending choice. Corruption decides which doors are open: under 40 only Dawnfire, 60 and over
# only the throne, and the band between may choose (CorruptionTiers). Both paths confirm first.
# Campaign overhaul: both final answers also raise flag.beat.throne_decided (mission 30's Milestone), and the
# opening line of the conversation remembers fork F6 (the vigil at the Knight's gate).
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
           _c("dlg.throne.c_dawn_yes", "", C.MISSING_FLAG, "flag.ending_embers", E.SET_FLAG, "flag.ending_dawnfire",
              do2=(E.SET_FLAG, "flag.beat.throne_decided")),
           _c("dlg.throne.c_wait", "throne")),
        _n("embers", "dlg.throne.embers",
           _c("dlg.throne.c_embers_yes", "", C.MISSING_FLAG, "flag.ending_dawnfire", E.SET_FLAG, "flag.ending_embers",
              do2=(E.SET_FLAG, "flag.beat.throne_decided")),
           _c("dlg.throne.c_wait", "throne")),
        # F6: the same opening, with the Knight's vigil remembered. Only the first line differs.
        _n("gate_kneel", "dlg.throne.gate_kneel",
           _c("dlg.throne.c_approach", "throne", C.HAS_FLAG, "flag.morthul_defeated"),
           _c("dlg.throne.c_leave", "", C.MISSING_FLAG, "flag.morthul_defeated")),
        _n("gate_draw", "dlg.throne.gate_draw",
           _c("dlg.throne.c_approach", "throne", C.HAS_FLAG, "flag.morthul_defeated"),
           _c("dlg.throne.c_leave", "", C.MISSING_FLAG, "flag.morthul_defeated")),
    ],
    start_variants=[_v("flag.rival.gate_kneel", "gate_kneel"), _v("flag.rival.gate_draw", "gate_draw")])


def build():
    return QUESTS, [ARCHIVIST, THRONE], [], "legacy"
