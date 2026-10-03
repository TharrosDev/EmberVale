"""Generator-side validation. Returns (errors, warnings); errors block a write and fail --check.

What it covers (the engine's --validate and CampaignGraphTests cover the rest):
  * spec hygiene: duplicate ids, id/flag prefixes, enum ordinals in range, refusing to overwrite a
    hand-authored file, per-objective-type rules, req/forbid flag clash
  * flag chain: every read has a writer (graph or code flag) that can run first (tolerant rule in
    graph.py), no unread completion flag except listed terminals
  * locale: every referenced key exists, no duplicate key, Pale Concord named only under pale.*
  * dialogue graphs (generated ones): start exists, gotos resolve, no unreachable node, no dead end
  * warnings only: ids that do not resolve in data/* (a location or NPC another workstream is adding)
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Dict, List, Set, Tuple

from . import graph as G
from . import locale as L
from .model import C, E, OT, Dialogue, Patch, Quest

MAX_COND, MAX_EFFECT = C.HAS_ITEM, E.BANNER
_ID_LINE = re.compile(r'^Id = "([a-z][a-z0-9_.]*)"', re.MULTILINE)


def id_index(root: Path) -> Set[str]:
    ids: Set[str] = set()
    for path in (root / "data").rglob("*.tres"):
        ids.update(_ID_LINE.findall(path.read_text(encoding="utf-8", errors="replace")))
    return ids


def check_quest(q: Quest, errors: List[str], warnings: List[str], known: Set[str], quest_ids: Set[str]) -> None:
    where = q.id
    if not q.id.startswith("quest."):
        errors.append(f"{where}: quest ids start with 'quest.'")
    if not q.title or not q.summary:
        errors.append(f"{where}: title and summary are required")
    if not q.objectives:
        errors.append(f"{where}: a quest needs at least one objective")
    if q.prerequisite and q.prerequisite not in quest_ids:
        errors.append(f"{where}: prerequisite '{q.prerequisite}' is not a known quest")
    for name, flag in (("completion_flag", q.completion_flag), ("auto_start", q.auto_start),
                       ("start_flag", q.start_flag), ("fail_flag", q.fail_flag)):
        if flag and not flag.startswith("flag."):
            errors.append(f"{where}: {name} '{flag}' must start with 'flag.'")
    if q.auto_start and q.auto_start == q.completion_flag:
        errors.append(f"{where}: auto_start equals its own completion flag")
    if q.is_main and q.completion_flag and not re.fullmatch(r"flag\.main\.[a-z0-9_]+_done", q.completion_flag) \
            and not q.ledger:
        warnings.append(f"{where}: completion flag '{q.completion_flag}' does not follow flag.main.<slug>_done")
    tags: Set[str] = set()
    for i, o in enumerate(q.objectives):
        check_objective(o, f"{where} objective {i}", errors, warnings, known)
        tag = o.tag or str(i + 1)
        if tag in tags:
            errors.append(f"{where}: duplicate objective tag '{tag}'")
        tags.add(tag)
    sub_ids = [o.sub_id or f"Obj_{i}" for i, o in enumerate(q.objectives)]
    if len(set(sub_ids)) != len(sub_ids):
        errors.append(f"{where}: duplicate objective sub-resource ids")


def check_objective(o, where: str, errors: List[str], warnings: List[str], known: Set[str]) -> None:
    if not 0 <= o.type <= OT.MILESTONE:
        errors.append(f"{where}: unknown objective type {o.type}")
        return
    if not o.text:
        errors.append(f"{where}: objective text is required")
    for name, flag in (("req_flag", o.req_flag), ("forbid_flag", o.forbid_flag),
                       ("completion_flag", o.completion_flag), ("activated_flag", o.activated_flag)):
        if flag and not flag.startswith("flag."):
            errors.append(f"{where}: {name} '{flag}' must start with 'flag.'")
    if o.req_flag and o.req_flag == o.forbid_flag:
        errors.append(f"{where}: the same flag is both required and forbidden (the objective could never be active)")
    if o.type == OT.REACH and o.location:
        errors.append(f"{where}: Reach takes its destination as the target; LocationId must be empty")
    if o.type == OT.ESCORT and not o.location:
        errors.append(f"{where}: Escort requires a destination location")
    if o.type == OT.MILESTONE and not o.target.startswith("flag."):
        errors.append(f"{where}: Milestone target must be a flag id, got '{o.target}'")
    if o.type == OT.DEFEND and o.count <= 0:
        errors.append(f"{where}: Defend needs a positive number of seconds")
    if o.type in (OT.KILL, OT.COLLECT, OT.TALK, OT.INTERACT) and not o.target:
        errors.append(f"{where}: missing target")
    for ident in (o.target if o.type in (OT.KILL, OT.COLLECT, OT.REACH, OT.TALK, OT.ESCORT, OT.DEFEND) else "", o.location):
        if ident and re.match(r"^(enemy|item|location|dialogue|companion)\.", ident) and ident not in known:
            warnings.append(f"{where}: id '{ident}' not found in data/ (is another workstream adding it?)")


def check_dialogue(d: Dialogue, errors: List[str], warnings: List[str], known: Set[str], quest_ids: Set[str]) -> None:
    where = d.id
    if not d.id.startswith("dialogue."):
        errors.append(f"{where}: dialogue ids start with 'dialogue.'")
    ids = [n.id for n in d.nodes]
    if len(set(ids)) != len(ids):
        errors.append(f"{where}: duplicate node ids")

    def check_pair(kind: str, ordinal: int, arg: str, limit: int, at: str) -> None:
        if not 0 <= ordinal <= limit:
            errors.append(f"{at}: {kind} ordinal {ordinal} out of range")
            return
        if kind == "effect":
            if ordinal in (E.SET_FLAG, E.CLEAR_FLAG) and not arg.startswith("flag."):
                errors.append(f"{at}: flag effect argument '{arg}' must start with 'flag.'")
            if ordinal == E.ADD_CORRUPTION and not re.fullmatch(r"-?\d+", arg):
                errors.append(f"{at}: AddCorruption argument '{arg}' is not an integer")
            if ordinal in (E.START_QUEST, E.TRACK_QUEST) and arg not in quest_ids:
                errors.append(f"{at}: quest '{arg}' is not a known quest")
            if ordinal in (E.ADD_REPUTATION, E.GIVE_ITEM, E.TAKE_ITEM) and not re.fullmatch(r"[a-z0-9_.]+:-?\d+", arg):
                errors.append(f"{at}: argument '{arg}' must be '<id>:<number>'")
        else:
            if ordinal in (C.HAS_FLAG, C.MISSING_FLAG) and not arg.startswith("flag."):
                errors.append(f"{at}: flag condition argument '{arg}' must start with 'flag.'")
            if ordinal in (C.CORRUPTION_AT_LEAST, C.CORRUPTION_BELOW) and not re.fullmatch(r"\d+", arg):
                errors.append(f"{at}: corruption threshold '{arg}' is not an integer")
            if ordinal in (C.REPUTATION_AT_LEAST, C.HAS_ITEM) and not re.fullmatch(r"[a-z0-9_.]+:\d+", arg):
                errors.append(f"{at}: argument '{arg}' must be '<id>:<number>'")
        if arg and re.match(r"^(item|faction|companion)\.", arg.split(":")[0]) and arg.split(":")[0] not in known:
            warnings.append(f"{at}: id '{arg.split(':')[0]}' not found in data/")

    for n in d.nodes:
        check_pair("effect", n.on_enter[0], n.on_enter[1], MAX_EFFECT, f"{where}/{n.id} onEnter")
        for j, c in enumerate(n.choices):
            at = f"{where}/{n.id}[{j}]"
            check_pair("condition", c.cond[0], c.cond[1], MAX_COND, at)
            check_pair("condition", c.cond2[0], c.cond2[1], MAX_COND, at)
            check_pair("effect", c.effect[0], c.effect[1], MAX_EFFECT, at)
            check_pair("effect", c.effect2[0], c.effect2[1], MAX_EFFECT, at)
    for v in d.start_variants:
        check_pair("condition", v.cond[0], v.cond[1], MAX_COND, f"{where} start variant")


def check_dialogue_graph(dd: dict, errors: List[str]) -> None:
    where = dd["id"]
    nodes = {n["id"]: n for n in dd["nodes"]}
    if dd["start"] not in nodes:
        errors.append(f"{where}: start node '{dd['start']}' does not exist")
    for v in dd["startVariants"]:
        if v["node"] not in nodes:
            errors.append(f"{where}: start variant targets unknown node '{v['node']}'")
    for n in dd["nodes"]:
        if n["choices"] == 0:
            errors.append(f"{where}: node '{n['id']}' is a dead end (no choices)")
        for g in n["gotos"]:
            if g not in nodes:
                errors.append(f"{where}: node '{n['id']}' goes to unknown node '{g}'")
    seen: Set[str] = set()
    stack = [dd["start"]] + [v["node"] for v in dd["startVariants"]]
    while stack:
        cur = stack.pop()
        if cur in seen or cur not in nodes:
            continue
        seen.add(cur)
        stack.extend(nodes[cur]["gotos"])
    for nid in nodes:
        if nid not in seen:
            errors.append(f"{where}: node '{nid}' is unreachable")


def validate(root: Path, campaign, lowered_quests: List[Quest], lowered_dialogues: List[Dialogue],
             patches: List[Patch], graph: dict, csv_text: str, hand_quest_ids: Dict[str, str],
             hand_dialogue_ids: Dict[str, str], target_of: Dict[str, str]) -> Tuple[List[str], List[str]]:
    """hand_*_ids: id -> relative path of existing files that are NOT generated (collision check).
    target_of: object id -> .tres relative path it will be written to."""
    errors: List[str] = []
    warnings: List[str] = []
    known = id_index(root)
    quest_ids = {q["id"] for q in graph["quests"]}

    seen: Dict[str, str] = {}
    for obj in list(lowered_quests) + list(lowered_dialogues):
        if obj.id in seen:
            errors.append(f"duplicate id '{obj.id}' ({seen[obj.id]} and {campaign.source.get(obj.id)})")
        seen[obj.id] = campaign.source.get(obj.id, "?")
        target = target_of[obj.id]
        if obj.id in hand_quest_ids and hand_quest_ids[obj.id] != target:
            errors.append(f"{obj.id}: already defined by hand-authored {hand_quest_ids[obj.id]}")
        if obj.id in hand_dialogue_ids and hand_dialogue_ids[obj.id] != target:
            errors.append(f"{obj.id}: already defined by hand-authored {hand_dialogue_ids[obj.id]}")
    paths = list(target_of.values())
    for p in set(paths):
        if paths.count(p) > 1:
            errors.append(f"two specs write {p}")

    for q in lowered_quests:
        check_quest(q, errors, warnings, known, quest_ids)
    for d in lowered_dialogues:
        check_dialogue(d, errors, warnings, known, quest_ids)
    for p in patches:
        if p.quest_id not in quest_ids:
            errors.append(f"patch: quest '{p.quest_id}' not found")
        for i, o in enumerate(p.append):
            check_objective(o, f"patch {p.quest_id} objective {i}", errors, warnings, known)

    generated = {d["id"]: d for d in graph["dialogues"] if d["source"] == "generated"}
    for d in generated.values():
        check_dialogue_graph(d, errors)

    scope = {q["id"] for q in graph["quests"] if q["source"] in ("generated", "patched") or q["isMain"]}
    errors += G.unordered_reads(graph, scope)
    errors += G.dead_end_flags(graph, scope)
    # reads from generated dialogues need a writer somewhere (graph or code)
    code = set(graph["codeFlags"])
    for d in generated.values():
        for f in d["flagsRead"]:
            if f not in code and not graph["flags"].get(f, {}).get("writers"):
                errors.append(f"{d['id']} reads {f} but nothing writes it")
    reach = G.reachable(graph)
    for q in graph["quests"]:
        if q["isMain"] and q["id"] not in reach:
            errors.append(f"{q['id']}: main quest is not reachable from New Game")
    for f in graph["forks"]:
        if not f["writers"]:
            errors.append(f"fork flag {f['flag']} is never set")
        if not f["readers"] and not f["externalRefs"]:
            errors.append(f"fork flag {f['flag']} has no consequence (nothing reads it)")

    # locale
    errors += L.audit(csv_text)
    catalog = L.catalog_keys(csv_text)
    for item in graph["quests"] + graph["dialogues"]:
        if item["source"] in ("generated", "patched"):
            for k in item["locKeys"]:
                if k not in catalog:
                    errors.append(f"{item['id']}: locale key '{k}' is not in strings.csv")
    return errors, warnings
