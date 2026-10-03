"""The machine-readable campaign graph: data/story/campaign_graph.json, plus the analyses over it.

The graph is computed from the FINAL .tres text of every quest (data/quests) and dialogue
(data/dialogue), with the generator's in-memory outputs overlaid, so it always describes exactly what
is on disk after a write. Hand-authored files are parsed with the same minimal reader as generated
ones (tools/campaign/tres.py).

Analysis (mirrored by tests/Embervale.Tests/CampaignGraphTests.cs; keep the two in step):

  reachable quests  Fixpoint. F = codeFlags + entry flags. A quest is reachable when its autoStart
      flag is in F, or (some dialogue starts it, or it is an entry quest) and its prerequisite is
      empty or reachable. Every reachable quest adds its completion, start and objective
      completion/activated flags to F; every dialogue adds the flags it sets once it is reachable
      (a dialogue is reachable when no quest owns it, or an owning quest is reachable). A quest owns a
      dialogue when one of its Talk objectives targets it.
  ancestors(Q)  Transitive closure of parents: the prerequisite quest, plus every quest (or owning
      quest of a dialogue) that writes Q's autoStart flag.
  writer ordering (tolerant)  A flag read by quest Q needs a writer that is a code flag, or Q itself,
      or an ancestor of Q, or a dialogue that is unowned or owned by Q or an ancestor of Q.
"""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Dict, List, Optional, Set

from . import tres
from .emit import is_generated
from .locale import looks_like_key

FLAG_COND = (5, 6)          # HasFlag, MissingFlag
EFFECT_SET, EFFECT_CLEAR, EFFECT_START_QUEST = 2, 3, 1
OT_TALK, OT_MILESTONE = 3, 8
_FLAG_LITERAL = re.compile(r'"(flag\.[A-Za-z0-9_.]+)"')
_COMMENT = re.compile(r"^\s*(;|//|\*|#|/\*)")


def _w(flag: str, via: str) -> dict:
    return {"flag": flag, "via": via}


# --- quests --------------------------------------------------------------------------------------

def parse_quest(text: str, rel: str, source: str) -> dict:
    secs = tres.parse(text)
    res = tres.resource_of(secs)
    subs = tres.sub_index(secs)
    p = res.props
    objectives = []
    for ref in p.get("Objectives", []):
        o = subs[ref].props
        objectives.append({
            "type": o.get("Type", 0), "target": o.get("TargetId", ""), "count": o.get("RequiredCount", 1),
            "location": o.get("LocationId", ""), "reqFlag": o.get("RequiredFlagId", ""),
            "forbidFlag": o.get("ForbiddenFlagId", ""), "completionFlag": o.get("CompletionFlagId", ""),
            "activatedFlag": o.get("ActivatedFlagId", ""), "optional": bool(o.get("IsOptional", False)),
            "description": o.get("Description", ""), "hintKey": o.get("HintKey", ""),
            "journalKey": o.get("JournalEntryKey", ""),
        })
    quest = {
        "id": p.get("Id", ""), "file": rel, "source": source, "title": p.get("Title", ""),
        "summary": p.get("Summary", ""), "chapter": p.get("ChapterKey", ""), "order": p.get("OrderInAct", 0),
        "region": p.get("RegionId", ""), "level": p.get("RecommendedLevel", 0),
        "isMain": bool(p.get("IsMainQuest", False)), "isLedger": bool(p.get("IsLedger", False)),
        "sequential": bool(p.get("SequentialObjectives", False)),
        "autoStart": p.get("AutoStartFlagId", ""), "completionFlag": p.get("CompletionFlagId", ""),
        "startFlag": p.get("StartFlagId", ""), "failFlag": p.get("FailFlagId", ""),
        "prerequisite": p.get("PrerequisiteQuestId", ""), "detailKey": p.get("DetailKey", ""),
        "giverKey": p.get("GiverNameKey", ""), "objectives": objectives,
    }
    writes, reads = [], []
    for field, via in (("completionFlag", "completion"), ("startFlag", "start"), ("failFlag", "fail")):
        if quest[field]:
            writes.append(_w(quest[field], via))
    if quest["autoStart"]:
        reads.append(_w(quest["autoStart"], "autoStart"))
    for i, o in enumerate(objectives):
        if o["completionFlag"]:
            writes.append(_w(o["completionFlag"], f"objective[{i}].completion"))
        if o["activatedFlag"]:
            writes.append(_w(o["activatedFlag"], f"objective[{i}].activated"))
        if o["reqFlag"]:
            reads.append(_w(o["reqFlag"], f"objective[{i}].required"))
        if o["forbidFlag"]:
            reads.append(_w(o["forbidFlag"], f"objective[{i}].forbidden"))
        if o["type"] == OT_MILESTONE and o["target"]:
            reads.append(_w(o["target"], f"objective[{i}].milestone"))
    quest["writes"], quest["reads"] = writes, reads
    keys = {quest["title"], quest["summary"], quest["detailKey"], quest["chapter"], quest["giverKey"]}
    for o in objectives:
        keys.update((o["description"], o["hintKey"], o["journalKey"]))
    quest["locKeys"] = sorted(k for k in keys if k and looks_like_key(k))
    return quest


# --- dialogues -----------------------------------------------------------------------------------

def parse_dialogue(text: str, rel: str, source: str) -> dict:
    secs = tres.parse(text)
    res = tres.resource_of(secs)
    ext = tres.ext_paths(secs)
    subs = tres.sub_index(secs)

    def kind(sec) -> str:
        return ext.get(sec.props.get("script", ""), "").rsplit("/", 1)[-1]

    nodes, sites, keys = [], [], set()
    sets, clears, reads, starts = set(), set(), set(), set()

    def read_site(flag, node, where):
        reads.add(flag)
        sites.append({"flag": flag, "kind": "read", "node": node, "at": where})

    def effect_site(eff, arg, node, where):
        if eff == EFFECT_SET and arg:
            sets.add(arg)
            sites.append({"flag": arg, "kind": "set", "node": node, "at": where})
        elif eff == EFFECT_CLEAR and arg:
            clears.add(arg)
            sites.append({"flag": arg, "kind": "clear", "node": node, "at": where})
        elif eff == EFFECT_START_QUEST and arg:
            starts.add(arg)

    for ref in res.props.get("Nodes", []):
        n = subs[ref].props
        nid = n.get("Id", "")
        gotos, count = [], 0
        for k in (n.get("Text", ""), n.get("Speaker", "")):
            keys.add(k)
        effect_site(n.get("OnEnterEffect", 0), n.get("OnEnterEffectArg", ""), nid, "onEnter")
        for j, cref in enumerate(n.get("Choices", [])):
            c = subs[cref].props
            count += 1
            keys.add(c.get("Text", ""))
            if c.get("Goto", ""):
                gotos.append(c["Goto"])
            for cond, arg in ((c.get("Condition", 0), c.get("ConditionArg", "")),
                              (c.get("Condition2", 0), c.get("Condition2Arg", ""))):
                if cond in FLAG_COND and arg:
                    read_site(arg, nid, f"choice[{j}]")
            effect_site(c.get("Effect", 0), c.get("EffectArg", ""), nid, f"choice[{j}]")
            effect_site(c.get("Effect2", 0), c.get("Effect2Arg", ""), nid, f"choice[{j}]")
        nodes.append({"id": nid, "choices": count, "gotos": gotos})
    variants = []
    for ref in res.props.get("StartVariants", []):
        v = subs[ref].props
        variants.append({"cond": v.get("Condition", 0), "arg": v.get("ConditionArg", ""), "node": v.get("NodeId", "")})
        if v.get("Condition", 0) in FLAG_COND and v.get("ConditionArg", ""):
            read_site(v["ConditionArg"], v.get("NodeId", ""), "startVariant")
    keys.add(res.props.get("SpeakerName", ""))
    return {
        "id": res.props.get("Id", ""), "file": rel, "source": source,
        "speaker": res.props.get("SpeakerName", ""), "start": res.props.get("StartNodeId", ""),
        "startVariants": variants, "nodes": nodes,
        "flagsSet": sorted(sets), "flagsCleared": sorted(clears), "flagsRead": sorted(reads),
        "questsStarted": sorted(starts), "sites": sites,
        "locKeys": sorted(k for k in keys if k and looks_like_key(k)),
    }


# --- the whole graph -----------------------------------------------------------------------------

def _external_refs(root: Path) -> Dict[str, Set[str]]:
    refs: Dict[str, Set[str]] = {}
    patterns = [("data", "*.tres"), ("scenes", "*.tscn"), ("src", "*.cs"), ("data/story", "*.json")]
    seen: Set[Path] = set()
    for base, pat in patterns:
        for path in sorted((root / base).rglob(pat)):
            rel = path.relative_to(root).as_posix()
            if path in seen or rel.startswith(("data/quests/", "data/dialogue/")) or rel.endswith("campaign_graph.json"):
                continue
            seen.add(path)
            for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
                if _COMMENT.match(line):
                    continue
                for flag in _FLAG_LITERAL.findall(line):
                    refs.setdefault(flag, set()).add(rel)
    return refs


def boss_flags(root: Path) -> List[str]:
    out = set()
    for path in (root / "data/bosses").glob("*.tres"):
        flag = tres.resource_of(tres.parse(path.read_text(encoding="utf-8"))).props.get("DefeatFlagId", "")
        if flag:
            out.add(flag)
    return sorted(out)


def build(root: Path, overrides: Dict[str, str], patched: Set[str],
          extra_code: Dict[str, str], extra_terminal: Dict[str, str], entry_quests: Dict[str, str]) -> dict:
    """overrides: posix path relative to root -> final text (generated/patched files not yet on disk)."""
    def read(rel: str, path: Optional[Path]) -> str:
        return overrides[rel] if rel in overrides else path.read_text(encoding="utf-8")

    quests, dialogues = [], []
    rels = {p.relative_to(root).as_posix(): p for sub in ("data/quests", "data/dialogue")
            for p in (root / sub).glob("*.tres")}
    for rel in sorted(set(rels) | set(k for k in overrides if k.startswith(("data/quests/", "data/dialogue/")))):
        text = read(rel, rels.get(rel))
        source = "generated" if is_generated(text) else ("patched" if rel in patched else "hand")
        if rel.startswith("data/quests/"):
            quests.append(parse_quest(text, rel, source))
        else:
            dialogues.append(parse_dialogue(text, rel, source))
    quests.sort(key=lambda x: x["id"])
    dialogues.sort(key=lambda x: x["id"])

    code = dict(extra_code)
    for f in boss_flags(root):
        code.setdefault(f, "boss defeat (data/bosses)")
    ext = _external_refs(root)

    flags: Dict[str, dict] = {}

    def entry(flag):
        return flags.setdefault(flag, {"writers": [], "readers": [], "code": flag in code, "externalRefs": []})

    for qd in quests:
        for w in qd["writes"]:
            entry(w["flag"])["writers"].append({"kind": "quest", "id": qd["id"], "via": w["via"]})
        for r in qd["reads"]:
            entry(r["flag"])["readers"].append({"kind": "quest", "id": qd["id"], "via": r["via"]})
    for dd in dialogues:
        for s in dd["sites"]:
            side = "writers" if s["kind"] in ("set", "clear") else "readers"
            if s["kind"] == "clear":
                continue
            entry(s["flag"])[side].append({"kind": "dialogue", "id": dd["id"], "via": f"{s['node']}/{s['at']}"})
    for f, e in flags.items():
        e["externalRefs"] = sorted(ext.get(f, ()))
    forks = [{"flag": f, **{k: e[k] for k in ("writers", "readers", "externalRefs")}}
             for f, e in sorted(flags.items()) if f.startswith("flag.fork.")]
    return {
        "schema": 1,
        "generator": "tools/gen_campaign.py",
        "codeFlags": sorted(code),
        "terminalFlags": sorted(extra_terminal),
        "entryQuests": sorted(entry_quests),
        "quests": quests,
        "dialogues": dialogues,
        "flags": {f: flags[f] for f in sorted(flags)},
        "forks": forks,
    }


def dumps(graph: dict) -> str:
    return json.dumps(graph, indent=1, ensure_ascii=False) + "\n"


# --- analysis ------------------------------------------------------------------------------------

def owners(graph: dict) -> Dict[str, Set[str]]:
    own: Dict[str, Set[str]] = {}
    for qd in graph["quests"]:
        for o in qd["objectives"]:
            if o["type"] == OT_TALK and o["target"]:
                own.setdefault(o["target"], set()).add(qd["id"])
    return own


def reachable(graph: dict) -> Set[str]:
    qs = {q["id"]: q for q in graph["quests"]}
    own = owners(graph)
    started_by_dialogue = {q for d in graph["dialogues"] for q in d["questsStarted"]}
    entry = set(graph["entryQuests"])
    flags = set(graph["codeFlags"])
    done: Set[str] = set()
    live_dialogues: Set[str] = set()
    changed = True
    while changed:
        changed = False
        for q in qs.values():
            if q["id"] in done:
                continue
            prereq_ok = not q["prerequisite"] or q["prerequisite"] in done
            if (q["autoStart"] and q["autoStart"] in flags) or \
                    ((q["id"] in started_by_dialogue or q["id"] in entry) and prereq_ok):
                done.add(q["id"])
                for w in q["writes"]:
                    if w["via"] != "fail":
                        flags.add(w["flag"])
                changed = True
        for d in graph["dialogues"]:
            if d["id"] in live_dialogues:
                continue
            if not own.get(d["id"]) or own[d["id"]] & done:
                live_dialogues.add(d["id"])
                flags.update(d["flagsSet"])
                changed = True
    return done


def ancestors(graph: dict) -> Dict[str, Set[str]]:
    qs = {q["id"]: q for q in graph["quests"]}
    own = owners(graph)
    writers_of: Dict[str, Set[str]] = {}
    for f, e in graph["flags"].items():
        for w in e["writers"]:
            ids = {w["id"]} if w["kind"] == "quest" else own.get(w["id"], set())
            writers_of.setdefault(f, set()).update(ids)
    parents: Dict[str, Set[str]] = {}
    for q in qs.values():
        ps = set()
        if q["prerequisite"] in qs:
            ps.add(q["prerequisite"])
        if q["autoStart"]:
            ps |= writers_of.get(q["autoStart"], set())
        ps.discard(q["id"])
        parents[q["id"]] = ps
    result: Dict[str, Set[str]] = {}
    for qid in qs:
        seen: Set[str] = set()
        stack = list(parents[qid])
        while stack:
            p = stack.pop()
            if p not in seen:
                seen.add(p)
                stack.extend(parents.get(p, ()))
        result[qid] = seen
    return result


def unordered_reads(graph: dict, scope: Set[str]) -> List[str]:
    """Quest reads (quests in `scope`) with no writer that could have run first. Tolerant rule above."""
    own = owners(graph)
    anc = ancestors(graph)
    code = set(graph["codeFlags"])
    problems = []
    for q in graph["quests"]:
        if q["id"] not in scope:
            continue
        ok_quests = anc[q["id"]] | {q["id"]}
        for r in q["reads"]:
            f = r["flag"]
            if f in code:
                continue
            good = False
            for w in graph["flags"].get(f, {}).get("writers", []):
                if w["kind"] == "quest":
                    good = w["id"] in ok_quests
                else:
                    o = own.get(w["id"], set())
                    good = not o or bool(o & ok_quests)
                if good:
                    break
            if not good:
                problems.append(f"{q['id']} reads {f} ({r['via']}) but no writer can run before it")
    return problems


def dead_end_flags(graph: dict, scope: Set[str]) -> List[str]:
    terminal = set(graph["terminalFlags"])
    problems = []
    for q in graph["quests"]:
        f = q["completionFlag"]
        if q["id"] not in scope or not f or f in terminal:
            continue
        e = graph["flags"].get(f, {})
        readers = [r for r in e.get("readers", []) if not (r["kind"] == "quest" and r["id"] == q["id"])]
        if not readers and not e.get("externalRefs"):
            problems.append(f"{q['id']} sets {f} but nothing reads it (chain gap; add the next link or list it in TERMINAL_FLAGS)")
    return problems
