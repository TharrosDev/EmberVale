#!/usr/bin/env python3
"""Append-only extension of HAND-AUTHORED dialogue .tres files (W-Spec-1).

The generator never rewrites a hand-authored dialogue. Where the campaign needs an existing NPC to say something
new, this tool appends to the file, and only appends:

    * new nodes (sub_resources before [resource], refs added to the Nodes array)
    * new choices added to an EXISTING node (sub_resources before the first node, refs inserted at an index)
    * new StartVariants (the DialogueStartVariant ext_resource and sub_resources are added when absent)

Nothing existing is renamed, reordered or rewritten; a node, choice or variant whose sub id is already in the
file is skipped, so running the tool twice changes nothing. The EXTENSIONS table and all prose live in
tools/campaign/specs/_s1_hand.py and _s1_hand2.py; the locale rows come out of the same table (specs/hand.py hands them to the
generator, which owns the strings.csv block).

    python tools/campaign/extend_hand.py            # apply
    python tools/campaign/extend_hand.py --check    # exit 1 when a file is missing an extension
"""

from __future__ import annotations

import re
import sys
import zlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Tuple

ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT))

from tools.campaign import emit, locale  # noqa: E402
from tools.campaign.model import Choice, Dialogue, Node, StartVariant  # noqa: E402

VARIANT_SCRIPT = emit.START_VARIANT_SCRIPT


@dataclass
class Extension:
    """What to append to one hand-authored dialogue."""
    file: str                                   # file name under data/dialogue
    dialogue_id: str                            # sanity check against the file's Id
    nodes: List[Node] = field(default_factory=list)
    # (existing node id, index to insert at, Choice). Index -1 appends before the last choice (usually the bye line).
    node_choices: List[Tuple[str, int, Choice]] = field(default_factory=list)
    variants: List[StartVariant] = field(default_factory=list)


def stem_of(dialogue_id: str) -> str:
    return dialogue_id[len("dialogue."):]


def lowered(ext: Extension):
    """Lower prose to keys. Returns (nodes, node_choices, rows). Pseudo nodes carry the choices added to existing
    nodes so their keys come out as dlg.<stem>.<node>.c_<tag>, exactly like a generated choice."""
    pseudo: Dict[str, Node] = {}
    for node_id, _idx, ch in ext.node_choices:
        pseudo.setdefault(node_id, Node(node_id, "", []))
        pseudo[node_id].choices.append(ch)
    d = Dialogue(id=ext.dialogue_id, speaker="", nodes=list(ext.nodes) + list(pseudo.values()))
    lw = locale._Lowerer()
    ld = lw.dialogue(d, None)
    by_id = {n.id: n for n in ld.nodes}
    nodes = [by_id[n.id] for n in ext.nodes]
    node_choices = []
    counters: Dict[str, int] = {}
    for node_id, idx, _ch in ext.node_choices:
        i = counters.get(node_id, 0)
        counters[node_id] = i + 1
        node_choices.append((node_id, idx, by_id[node_id].choices[i]))
    return nodes, node_choices, lw.rows


def all_rows(extensions: List[Extension]) -> List[Tuple[str, str]]:
    rows: List[Tuple[str, str]] = []
    for ext in extensions:
        rows += lowered(ext)[2]
    return rows


# --- text surgery ---------------------------------------------------------------------------------

_SUB = re.compile(r'^\[sub_resource type="Resource" id="([^"]+)"\]\s*$')


def _ext_id(lines: List[str], script: str):
    for ln in lines:
        m = re.match(r'\[ext_resource type="Script" path="[^"]*' + re.escape(script) + r'" id="([^"]+)"\]', ln)
        if m:
            return m.group(1)
    return None


def _block_end(lines: List[str], start: int) -> int:
    """Index just past the sub_resource/resource block starting at `start` (up to the next header)."""
    j = start + 1
    while j < len(lines) and not lines[j].startswith("["):
        j += 1
    return j


def apply_text(text: str, ext: Extension) -> str:
    crlf = "\r\n" in text
    lines = text.replace("\r\n", "\n").split("\n")
    res_idx = next(i for i, ln in enumerate(lines) if ln.strip() == "[resource]")
    res_id = next((ln.split('"')[1] for ln in lines[res_idx:] if ln.startswith("Id = ")), "")
    if res_id != ext.dialogue_id:
        raise SystemExit(f"{ext.file}: holds {res_id!r}, not {ext.dialogue_id!r}")
    node_ext = _ext_id(lines, "DialogueNode.cs")
    choice_ext = _ext_id(lines, "DialogueChoice.cs")
    if not node_ext or not choice_ext:
        raise SystemExit(f"{ext.file}: no node/choice script ext_resource")

    nodes, node_choices, _rows = lowered(ext)
    existing = {m.group(1) for ln in lines if (m := _SUB.match(ln))}

    def sub_lines(block: str) -> List[str]:
        return block.replace('ExtResource("3_choice")', f'ExtResource("{choice_ext}")') \
                    .replace('ExtResource("2_node")', f'ExtResource("{node_ext}")').rstrip("\n").split("\n") + [""]

    new_choice_blocks: List[str] = []
    new_node_blocks: List[str] = []
    new_variant_blocks: List[str] = []
    choice_refs: Dict[str, List[Tuple[int, str]]] = {}
    for node_id, idx, ch in node_choices:
        cid = f"ch_{node_id}_{ch.tag}"
        if cid not in existing:
            new_choice_blocks += sub_lines(emit._choice_block(ch, cid))
            existing.add(cid)
        choice_refs.setdefault(node_id, []).append((idx, cid))
    node_refs: List[str] = []
    for n in nodes:
        nid = f"node_{n.id}"
        crefs = []
        for ch in n.choices:
            cid = f"ch_{n.id}_{ch.tag}"
            if cid not in existing:
                new_choice_blocks += sub_lines(emit._choice_block(ch, cid))
                existing.add(cid)
            crefs.append(f'SubResource("{cid}")')
        if nid not in existing:
            new_node_blocks += sub_lines(emit._node_block(n, crefs))
            existing.add(nid)
            node_refs.append(f'SubResource("{nid}")')
    variant_refs: List[str] = []
    for v in ext.variants:
        # Stable id independent of order: the node, the condition kind and a checksum of its argument.
        vid = f"start_{v.node}_{v.cond[0]}_{zlib.crc32(v.cond[1].encode()) & 0xFFFF:04x}"
        if vid in existing:
            continue
        new_variant_blocks += [f'[sub_resource type="Resource" id="{vid}"]', 'script = ExtResource("9_variant")',
                               f"Condition = {v.cond[0]}", f"ConditionArg = {emit.q(v.cond[1])}",
                               f"NodeId = {emit.q(v.node)}", ""]
        existing.add(vid)
        variant_refs.append(f'SubResource("{vid}")')

    if not (new_choice_blocks or new_node_blocks or new_variant_blocks or choice_refs):
        return text

    # 1) Existing nodes gain choice refs (idempotent: only refs not already present are added).
    for node_id, refs in choice_refs.items():
        start = next((i for i, ln in enumerate(lines) if ln.strip() == f'[sub_resource type="Resource" id="node_{node_id}"]'), None)
        if start is None:
            raise SystemExit(f"{ext.file}: node {node_id!r} not found")
        end = _block_end(lines, start)
        for i in range(start, end):
            m = re.fullmatch(r"Choices = Array\[Resource\]\(\[(.*)\]\)", lines[i])
            if not m:
                continue
            cur = [s.strip() for s in m.group(1).split(", ") if s.strip()]
            for idx, cid in refs:
                ref = f'SubResource("{cid}")'
                if ref in cur:
                    continue
                pos = len(cur) + idx if idx < 0 else idx
                cur.insert(max(0, min(pos, len(cur))), ref)
            lines[i] = f"Choices = Array[Resource]([{', '.join(cur)}])"
            break
        else:
            raise SystemExit(f"{ext.file}: node {node_id!r} has no Choices line")

    # 2) New choice sub_resources go before the first node sub_resource, so every ref resolves.
    first_node = next(i for i, ln in enumerate(lines)
                      if _SUB.match(ln) and any(l2 == f'script = ExtResource("{node_ext}")' for l2 in lines[i + 1:i + 3]))
    lines[first_node:first_node] = new_choice_blocks

    # 3) New nodes and variants go just before [resource].
    res_idx = next(i for i, ln in enumerate(lines) if ln.strip() == "[resource]")
    lines[res_idx:res_idx] = new_node_blocks + new_variant_blocks
    res_idx = next(i for i, ln in enumerate(lines) if ln.strip() == "[resource]")

    # 4) The Nodes array, and the StartVariants property.
    for i in range(res_idx, len(lines)):
        m = re.fullmatch(r"Nodes = Array\[Resource\]\(\[(.*)\]\)", lines[i])
        if m:
            cur = [s.strip() for s in m.group(1).split(", ") if s.strip()]
            cur += [r for r in node_refs if r not in cur]
            lines[i] = f"Nodes = Array[Resource]([{', '.join(cur)}])"
            nodes_line = i
            break
    else:
        raise SystemExit(f"{ext.file}: no Nodes line")
    if variant_refs:
        for i in range(res_idx, len(lines)):
            m = re.fullmatch(r"StartVariants = Array\[Resource\]\(\[(.*)\]\)", lines[i])
            if m:
                cur = [s.strip() for s in m.group(1).split(", ") if s.strip()]
                cur += [r for r in variant_refs if r not in cur]
                lines[i] = f"StartVariants = Array[Resource]([{', '.join(cur)}])"
                break
        else:
            lines.insert(nodes_line + 1, f"StartVariants = Array[Resource]([{', '.join(variant_refs)}])")
        if not any('DialogueStartVariant.cs' in ln for ln in lines):
            last_ext = max(i for i, ln in enumerate(lines) if ln.startswith("[ext_resource"))
            lines.insert(last_ext + 1, f'[ext_resource type="Script" path="{VARIANT_SCRIPT}" id="9_variant"]')

    # 5) load_steps, when the header carries it.
    subs_added = sum(1 for ln in new_choice_blocks + new_node_blocks + new_variant_blocks if ln.startswith("[sub_resource"))
    if "load_steps=" in lines[0]:
        lines[0] = re.sub(r"load_steps=(\d+)", lambda m: f"load_steps={int(m.group(1)) + subs_added + (1 if variant_refs else 0)}", lines[0])

    out = "\n".join(lines)
    return out.replace("\n", "\r\n") if crlf else out


def main(argv=None) -> int:
    from tools.campaign.specs import _s1_hand, _s1_hand2
    check = "--check" in (argv or sys.argv[1:])
    status = 0
    for ext in _s1_hand.EXTENSIONS + _s1_hand2.EXTENSIONS:
        path = ROOT / "data" / "dialogue" / ext.file
        with open(path, encoding="utf-8", newline="") as f:
            text = f.read()
        new = apply_text(text, ext)
        if new == text:
            print(f"ok       {ext.file}")
            continue
        if check:
            print(f"MISSING  {ext.file}: extensions not applied")
            status = 1
            continue
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(new)
        print(f"extended {ext.file}")
    return status


if __name__ == "__main__":
    sys.exit(main())
