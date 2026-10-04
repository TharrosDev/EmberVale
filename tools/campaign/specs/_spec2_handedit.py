"""W-Spec-2 edits to HAND-AUTHORED data: four Undying dialogues, the Annexe Keeper, the Ashen Knight's last
words, and three boss epithets. Not a spec (leading underscore). Run it directly after editing the tables
below; it is idempotent and append-only:

    python tools/campaign/specs/_spec2_handedit.py            # apply
    python tools/campaign/specs/_spec2_handedit.py --check    # exit 1 if a file is not yet patched

New dialogue nodes are appended, existing node ids and texts never change, an existing node may gain
extra choices at the END of its list, and start variants are added to the resource. ROWS is the locale
text the patches reference; spec2_pale.py writes it into the spec2 block of strings.csv.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT))

from tools.campaign import emit  # noqa: E402
from tools.campaign.model import C, E, Choice, Node, StartVariant  # noqa: E402


def t(text: str) -> str:
    return text.replace("<<", '"').replace(">>", '"')


ROWS: list = []


def row(key: str, text: str) -> str:
    ROWS.append((key, t(text)))
    return key


# --------------------------------------------------------------------------------------------------
# The four undying residents of Vesperhold (pale.dlg.<npc>.*): clue, recoil, F5 variants, F5 aftermath
# --------------------------------------------------------------------------------------------------
UNDYING = {
    "steward": dict(
        clue="The Steward begins the proclamation before you can speak, and stops halfway, because you are not listening. <<You want the count. Everyone who comes through that gate wants the count, in the end. Corwen keeps the ledger under the clock, but he will not open it to a stranger who has not met the lamps. Speak to Oswin first. The lamps are older than the ledger, and he lights them.>>",
        recoil="The proclamation stops in the Steward's throat. She has seen that look before, on the only person who ever signed for all of them. <<You carry her cold,>> she says, <<or something that wants to learn it. Forgive me, I will not offer you my hand. I do not know whose it would be.>>",
        released="The Steward is sitting on the well's edge with the proclamation open on her knee, and she is not reading it. <<I have a headache,>> she says wonderingly. <<A headache. Oswin says his knees hurt. The stone has let go of us, and I find I do not know what to do with an afternoon. The Queen is at the Court. Go, if you are going. Go quickly, for her sake.>>",
        kept="The Steward is reading the proclamation to the empty plaza, level and loud, because it is still evening and the count still stands. <<You kept it,>> she says without turning, <<and I thank you, and I am ashamed of how glad I am. The Queen is waiting at the Court. She has always been waiting.>>",
        freed_released="<<It came slowly, the way it should have. A grey hair on Tuesday. A knee that aches in the rain. I am old now, Seventh, a little, and growing older by the hour, and I would not trade one hour of it back.>>",
        freed_kept="<<It came all at once, the years. Half of the street sat down on their steps and did not get up. I do not blame you. I do not forgive you. I read their names every dusk now, the ones who sat down, and I am grateful for the count that let me say goodbye.>>"),
    "lamplighter": dict(
        clue="Oswin does not look up from the wick he is trimming. <<Ismene sent you. She sends all of them. Three of my lamps are older than the rest, and they remember things, if you touch them at the wrong hour. It is always the wrong hour here. Maren has the other half of it. She bakes for the count.>>",
        recoil="Oswin steps back so fast that he drops his wick. Every flame in the plaza leans away from you at once. <<I have lit these lamps for four hundred years, and not one of them has ever done that. Whatever you carry, friend, it is looking for a bargain. Take it elsewhere.>>",
        released="Oswin has his daughter on his shoulder. She is heavier than she was yesterday, and he cannot stop saying so. <<She has gained an ounce. I weighed her on the baker's scale. An ounce, friend. Go and do what you must. I will light the lamps tonight and let them go out.>>",
        kept="Oswin is lighting the lamps one by one, the way he always does, but his hand shakes. <<Same evening. Same lamps. My daughter is the same weight she was yesterday.>> He does not say the rest. <<Go on. Someone should finish this. I cannot, and I do not think I would want to.>>",
        freed_released="<<She turned over in her sleep last night. Turned over, by herself, for the first time. I sat up until morning and cried like a child, and then I started on a bigger cradle.>>",
        freed_kept="<<It was a hard night. She woke up crying, hungry, only hungry, and I held her until I stopped shaking. Half of this plaza sat down on its steps. I lit the lamps for them one last time. It is the only right thing I have done in four hundred years.>>"),
    "baker": dict(
        clue="Maren does not stop kneading. <<Every dusk the ovens go warm on their own. Eleven hundred and four loaves' worth of heat, and not one loaf in them. That is her counting, Seventh. You can feel it in the walls. Go to Corwen. He wrote the numbers down once, and he is the only one of us who kept a copy of the terms.>>",
        recoil="Maren's hands go still in the dough. <<You smell of the ovens before they warm. Of that hour. I would know it anywhere.>> She does not look at you. <<Is it the Court you are walking toward, or are you walking out of it?>>",
        released="Maren has flour on her face and has started to cry, quite badly, over a loaf that has gone slightly stale. <<I have waited so long for something to go off.>> She wipes her hands. <<Go and finish it. We will bake for whatever comes after.>>",
        kept="Maren is kneading. The dough is the dough it has always been. <<You kept it,>> she says to the bread. <<Well. Somebody had to say yes to it again.>> The ovens are warm, and nobody has lit them.",
        freed_released="<<The loaves go off in a day now. I bake six and eat two. Last night I burnt one on purpose, to see what it smelled like. It smelled like Tuesday.>>",
        freed_kept="<<The ovens went cold in the night, all at once, and I knew. I baked one loaf for each of the ones who sat down. There are a great many loaves. We ate them standing, in the plaza. I would not call it a mercy. I would call it bread.>>"),
    "clockkeeper": dict(
        clue="Corwen does not stop winding. <<You want the registry. It is on the lectern by my door. Read the margin before you read the names: the names are only ours, the margin is hers. And when you have read it, stand by the three old lamps, one after another. You will understand why she cannot stop. I have never been able to hate her since.>>",
        recoil="Corwen's key stops in the winding-hole. <<The hand moved when you came through the gate. Just a hair. It has not moved in four hundred years, and it moved for you. I do not know what that means, and I would very much like not to find out.>>",
        released="Corwen has stopped winding. The hand of the clock is trembling, a hair past five and twenty past five. <<It is going to move,>> he says. <<I can feel it in the spring. When it does I think I shall be a very old man. Thank you for that. Go and do the rest.>>",
        kept="Corwen is winding. The hand does not move. <<Five and twenty past five,>> he says gently, <<for a little longer. You have made us a gift of the evening. I would ask you not to make a habit of such gifts. Go on, Seventh. Finish what the stone began.>>",
        freed_released="<<It moved, the hand. Six o'clock, and then ten past, and then it simply went on, as if it had never stopped. I wound it once more out of habit. I do not think it needs me.>>",
        freed_kept="<<It struck every hour it had missed, one after another, until noon. Then it stopped, and the street was very quiet, and it was a Tuesday. I stood there a long time. I do not know whether to thank you, Seventh. I wound it anyway.>>"),
}

C_RECOIL_ASK = row("pale.dlg.undying.c_recoil_ask", "I need to understand the count.")
C_FREED_HOW = row("pale.dlg.undying.c_freed_how", "And how is it out here, now?")


def _undying_plan(npc: str, data: dict) -> dict:
    stem = f"pale.dlg.{npc}"
    keys = {k: row(f"{stem}.v_{k}" if k in ("clue", "recoil", "released", "kept") else f"{stem}.{k}", data[k])
            for k in data}
    back, bye = f"{stem}.c_back", f"{stem}.c_bye"
    nodes = [
        Node("v_clue", keys["clue"], [Choice(back, "root", tag="back"), Choice(bye, "", tag="bye")]),
        Node("v_recoil", keys["recoil"], [Choice(C_RECOIL_ASK, "v_clue", tag="ask"), Choice(bye, "", tag="bye")]),
        Node("v_released", keys["released"], [Choice(back, "root", tag="back"), Choice(bye, "", tag="bye")]),
        Node("v_kept", keys["kept"], [Choice(back, "root", tag="back"), Choice(bye, "", tag="bye")]),
        Node("freed_released", keys["freed_released"], [Choice(bye, "", tag="bye")]),
        Node("freed_kept", keys["freed_kept"], [Choice(bye, "", tag="bye")]),
    ]
    variants = [
        StartVariant((C.HAS_FLAG, "flag.hollow_queen_defeated"), "root"),
        StartVariant((C.HAS_FLAG, "flag.fork.queen_released"), "v_released"),
        StartVariant((C.HAS_FLAG, "flag.fork.queen_kept"), "v_kept"),
        StartVariant((C.CORRUPTION_AT_LEAST, "40"), "v_recoil"),
        StartVariant((C.HAS_FLAG, "flag.main.pale_door_done"), "v_clue"),
    ]
    extend = {"freed": [
        Choice(C_FREED_HOW, "freed_released", cond=(C.HAS_FLAG, "flag.fork.queen_released"), tag="how_released"),
        Choice(C_FREED_HOW, "freed_kept", cond=(C.HAS_FLAG, "flag.fork.queen_kept"), tag="how_kept"),
    ]}
    return {"file": f"Undying{ {'steward': 'Steward', 'lamplighter': 'Lamplighter', 'baker': 'Baker', 'clockkeeper': 'Clockkeeper'}[npc] }.tres",
            "nodes": nodes, "variants": variants, "extend": extend}


PLANS = [_undying_plan(npc, data) for npc, data in UNDYING.items()]

# --------------------------------------------------------------------------------------------------
# Keeper Ysolde Marr (the Annexe): mission 23's hand-in, with a member's shortcut
# --------------------------------------------------------------------------------------------------
K_PAGES = row("dlg.archive_keeper.pages_key", "She reads your face before she reads your hands. <<Seren sent you. She sends nobody. And you have read the five accounts together at her table, which the Archive tried to forbid and then forgot to.>> Her hands come apart. <<The key to the deep stacks is not a key. It is a sentence, split in two. She keeps the first half and I keep the second, and neither of us may say ours except to someone who has heard all five. I will say mine once.>> She tells you the sentence. You find that you cannot forget it. <<What is in that vault is why we exist. Do not tell me what it says.>>")
K_WHY = row("dlg.archive_keeper.pages_key_why", "<<The Archive is a thousand people who each know a little of it, and none of them the whole. It is the only reason any of it has survived. Do not make me the exception.>>")
K_MEMBER = row("dlg.archive_keeper.pages_key_member", "She looks at the ring and then at you. <<Then you will not need the lecture. Take this as well, from the Annexe stores. The stacks bite, and a member's life is on our books.>>")
K_C_WHY = row("dlg.archive_keeper.c_pages_why", "Does the Archive know what is down there?")
K_C_RING = row("dlg.archive_keeper.c_pages_ring", "(Show your member's ring.)")
K_C_KEEP = row("dlg.archive_keeper.c_pages_keep", "I will keep it safe.")
K_C_THANKS = row("dlg.archive_keeper.c_pages_thanks", "Thank you.")

PLANS.append({
    "file": "ArchiveKeeper.tres",
    "nodes": [
        Node("pages_key", K_PAGES, [
            Choice(K_C_WHY, "pages_key_why", tag="why"),
            Choice(K_C_RING, "pages_key_member", cond=(C.GUILD_RANK_AT_LEAST, "faction.veiled_archive:1"),
                   effect=(E.GIVE_ITEM, "item.potion.health:2"), tag="ring"),
            Choice(K_C_KEEP, "", tag="keep")],
             on_enter=(E.SET_FLAG, "flag.beat.stacks_key_given")),
        Node("pages_key_why", K_WHY, [Choice(K_C_KEEP, "", tag="keep")]),
        Node("pages_key_member", K_MEMBER, [Choice(K_C_THANKS, "", tag="thanks")]),
    ],
    "variants": [
        StartVariant((C.HAS_FLAG, "flag.beat.stacks_key_given"), "root"),
        StartVariant((C.HAS_FLAG, "flag.beat.pages_read"), "pages_key"),
    ],
    "extend": {},
})

# --------------------------------------------------------------------------------------------------
# The Ashen Knight's last words: what he remembers of the library parley and the vigil at the gate
# --------------------------------------------------------------------------------------------------
A_LIB_TRUST = row("dlg.ashenknight.absorb.library_trust", "<<You read it beside me. I did not stop you, and I did not know what I would feel. Relief, as it turns out. I was tired of keeping a secret from the only person who could use it.>>")
A_LIB_DEFY = row("dlg.ashenknight.absorb.library_defy", "<<You read it alone, over my objection. Good. Whoever sits there must be able to read over someone's objection. I hope you were able to carry what it said.>>")
A_GATE_KNEEL = row("dlg.ashenknight.absorb.gate_kneel", "<<You knelt, at the gate. Nobody has knelt in front of me since the last of my five. I asked you not to do it twice, and you have not. That is all I ever wanted of you.>>")
A_GATE_DRAW = row("dlg.ashenknight.absorb.gate_draw", "<<You drew. I was afraid you would not. A drawn blade means you are still able to refuse. Keep that, Seventh, whatever else you keep.>>")
A_C_LIB_TRUST = row("dlg.ashenknight.absorb.c_library_trust", "In the stacks, you let me read beside you.")
A_C_LIB_DEFY = row("dlg.ashenknight.absorb.c_library_defy", "In the stacks, I read over your objection.")
A_C_GATE_KNEEL = row("dlg.ashenknight.absorb.c_gate_kneel", "At the gate, I knelt with you.")
A_C_GATE_DRAW = row("dlg.ashenknight.absorb.c_gate_draw", "At the gate, I drew on you.")
A_C_BACK = "dlg.ashenknight.absorb.c_listen"      # existing key, the same back-link the duel nodes use

PLANS.append({
    "file": "AshenKnightAbsorb.tres",
    "nodes": [
        Node("library_trust", A_LIB_TRUST, [Choice(A_C_BACK, "last_words_2", tag="back")]),
        Node("library_defy", A_LIB_DEFY, [Choice(A_C_BACK, "last_words_2", tag="back")]),
        Node("gate_kneel", A_GATE_KNEEL, [Choice(A_C_BACK, "last_words_2", tag="back")]),
        Node("gate_draw", A_GATE_DRAW, [Choice(A_C_BACK, "last_words_2", tag="back")]),
    ],
    "variants": [],
    "extend": {"last_words": [
        Choice(A_C_LIB_TRUST, "library_trust", cond=(C.HAS_FLAG, "flag.rival.library_trust"), tag="library_trust"),
        Choice(A_C_LIB_DEFY, "library_defy", cond=(C.HAS_FLAG, "flag.rival.library_defy"), tag="library_defy"),
        Choice(A_C_GATE_KNEEL, "gate_kneel", cond=(C.HAS_FLAG, "flag.rival.gate_kneel"), tag="gate_kneel"),
        Choice(A_C_GATE_DRAW, "gate_draw", cond=(C.HAS_FLAG, "flag.rival.gate_draw"), tag="gate_draw"),
    ]},
})

# --------------------------------------------------------------------------------------------------
# Boss epithets and intro lines (BossResource.EpithetKey / IntroLineKey)
# --------------------------------------------------------------------------------------------------
BOSSES = {
    "HollowQueen.tres": (
        row("boss.hollow_queen.epithet", "She Who Counts the Evening"),
        row("boss.hollow_queen.intro", "Eleven hundred and four. And one more, tonight.")),
    "AshenKnight.tres": (
        row("boss.ashen_knight.epithet", "Keeper of the Throne"),
        row("boss.ashen_knight.intro", "I have waited four hundred years for someone worth drawing on. Do not disappoint me, Seventh.")),
    "Morthul.tres": (
        row("boss.morthul.epithet", "The Ash King"),
        row("boss.morthul.intro", "Sit, little ember. Everyone does, in the end.")),
}


# --------------------------------------------------------------------------------------------------
# The patcher
# --------------------------------------------------------------------------------------------------
def _choice_blocks(node_id: str, choices, prefix: str):
    blocks, refs = [], []
    for j, c in enumerate(choices):
        cid = f"ch_s2_{node_id}_{prefix}{j}"
        blocks.append(emit._choice_block(c, cid))
        refs.append(f'SubResource("{cid}")')
    return blocks, refs


def patch_dialogue(path: Path, plan: dict, write: bool) -> bool:
    text = path.read_text(encoding="utf-8")
    if plan["nodes"] and f'Id = "{plan["nodes"][0].id}"' in text:
        return False                      # already patched
    nl = "\r\n" if "\r\n" in text else "\n"
    text = text.replace("\r\n", "\n")

    # 1. extend existing nodes: new choice blocks go right before the node block that will reference them
    for node_id, extra in plan["extend"].items():
        blocks, refs = _choice_blocks(node_id, extra, "x")
        m = re.search(rf'\[sub_resource type="Resource" id="node_{node_id}"\]\n.*?\nChoices = Array\[Resource\]\(\[(.*?)\]\)\n',
                      text, re.S)
        if not m:
            raise SystemExit(f"{path.name}: node '{node_id}' not found")
        old_list = m.group(1)
        # The new choices go before the node's last existing one (its "leave" / fallthrough line).
        if ", " in old_list:
            head, last = old_list.rsplit(", ", 1)
            merged = f'{head}, {", ".join(refs)}, {last}'
        else:
            merged = f'{", ".join(refs)}, {old_list}'
        new_line = f'Choices = Array[Resource]([{merged}])\n'
        block_start = text.rfind('[sub_resource type="Resource" id="node_' + node_id + '"]', 0, m.end())
        block = text[block_start:m.end()].replace(m.group(0)[m.group(0).index("Choices = "):], new_line)
        text = text[:block_start] + "\n".join(blocks) + "\n" + block + text[m.end():]

    # 2. new nodes and their choices, just before [resource]
    new_blocks, node_refs = [], []
    for n in plan["nodes"]:
        c_blocks, c_refs = _choice_blocks(n.id, n.choices, "")
        new_blocks += c_blocks
        new_blocks.append(emit._node_block(n, c_refs))
        node_refs.append(f'SubResource("node_{n.id}")')
    variant_blocks, variant_refs = [], []
    for i, v in enumerate(plan["variants"]):
        variant_blocks.append(f'[sub_resource type="Resource" id="start_{i}"]\nscript = ExtResource("4_variant")\n'
                              f"Condition = {v.cond[0]}\nConditionArg = {emit.q(v.cond[1])}\nNodeId = {emit.q(v.node)}\n")
        variant_refs.append(f'SubResource("start_{i}")')
    marker = "\n[resource]\n"
    head, tail = text.split(marker)
    head = head.rstrip("\n") + "\n\n" + "\n".join(new_blocks + variant_blocks)
    tail = re.sub(r"(Nodes = Array\[Resource\]\(\[.*?)\]\)",
                  lambda m: m.group(1) + ", " + ", ".join(node_refs) + "])", tail, count=1)
    if variant_refs:
        tail = tail.rstrip("\n") + f"\nStartVariants = Array[Resource]([{', '.join(variant_refs)}])\n"
        if 'id="4_variant"]' not in head:
            head = head.replace('id="3_choice"]\n', 'id="3_choice"]\n' +
                                f'[ext_resource type="Script" path="{emit.START_VARIANT_SCRIPT}" id="4_variant"]\n', 1)
    out = (head.rstrip("\n") + "\n" + marker + tail).replace("\n", nl)
    if write:
        path.write_text(out, encoding="utf-8", newline="")
    return True


def patch_boss(path: Path, epithet: str, intro: str, write: bool) -> bool:
    text = path.read_text(encoding="utf-8")
    if "EpithetKey" in text:
        return False
    nl = "\r\n" if "\r\n" in text else "\n"
    out = text.rstrip("\r\n") + nl + f'EpithetKey = "{epithet}"' + nl + f'IntroLineKey = "{intro}"' + nl
    if write:
        path.write_text(out, encoding="utf-8", newline="")
    return True


def main(check: bool) -> int:
    pending = []
    for plan in PLANS:
        path = ROOT / "data/dialogue" / plan["file"]
        if patch_dialogue(path, plan, write=not check):
            pending.append(plan["file"])
    for name, (epithet, intro) in BOSSES.items():
        if patch_boss(ROOT / "data/bosses" / name, epithet, intro, write=not check):
            pending.append(name)
    if check:
        for name in pending:
            print(f"not patched: {name}")
        return 1 if pending else 0
    print(f"patched {len(pending)} file(s): {', '.join(pending) or 'none (already up to date)'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main("--check" in sys.argv))
