#!/usr/bin/env python3
"""Data-driven campaign generator (replaces the hard-coded tools/gen_main_story.py).

    python tools/gen_campaign.py                 # validate, then write quests, dialogues, locale blocks, graph
    python tools/gen_campaign.py --check         # regenerate in memory, diff against disk, exit 1 on drift or errors
    python tools/gen_campaign.py --graph-only    # (re)write only data/story/campaign_graph.json
    python tools/gen_campaign.py --list          # what each spec produces

Specs live in tools/campaign/specs/*.py (see docs/playbook/campaign-authoring.md). Order of work:
discover specs -> lower prose to locale keys -> emit .tres into data/quests and data/dialogue ->
patch hand-authored quests -> rewrite each spec tag's locale block -> compute the campaign graph ->
validate everything -> write. Nothing is written when validation fails.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Dict, List

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from tools.campaign import config, emit, graph, locale, registry, tres, validate  # noqa: E402

LOCALE = "data/locale/strings.csv"
GRAPH = "data/story/campaign_graph.json"


def _read(path: Path) -> str:
    with open(path, encoding="utf-8", newline="") as f:
        return f.read()


def _write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def _norm(text: str) -> str:
    return text.replace("\r\n", "\n")


class Result:
    def __init__(self) -> None:
        self.files: Dict[str, str] = {}        # rel path -> final text (every file this tool owns)
        self.patched: set = set()
        self.errors: List[str] = []
        self.warnings: List[str] = []
        self.campaign = None
        self.summary: List[str] = []


def compute(root: Path = ROOT, only: List[str] = None, extra_modules=()) -> Result:
    r = Result()
    c = registry.discover(only, extra_modules)
    r.campaign = c
    rows_by_tag: Dict[str, list] = {t: [] for t in c.tags}
    lowered_q, lowered_d, lowered_p = [], [], []
    target_of: Dict[str, str] = {}

    for tag in c.tags:
        qs = [q for q in c.quests if c.tag_of[q.id] == tag]
        ps = [p for p in c.patches if c.tag_of[f"patch:{p.quest_id}"] == tag]
        ds = [d for d in c.dialogues if c.tag_of[d.id] == tag]
        try:
            lq, lp, ld, rows = locale.lower(qs, ps, ds)
        except ValueError as exc:
            r.errors.append(str(exc))
            continue
        lowered_q += lq
        lowered_p += lp
        lowered_d += ld
        rows_by_tag[tag] = rows + c.manual_rows.get(tag, [])

    for q in lowered_q:
        rel = f"data/quests/{emit.quest_file(q)}"
        target_of[q.id] = rel
        r.files[rel] = emit.quest_tres(q, c.source[q.id])
    for d in lowered_d:
        rel = f"data/dialogue/{emit.dialogue_file(d)}"
        target_of[d.id] = rel
        r.files[rel] = emit.dialogue_tres(d, c.source[d.id])

    # Refuse to overwrite a hand-authored file; collect hand-authored ids for the collision check.
    hand_quests, hand_dialogues = {}, {}
    for sub, bucket in (("quests", hand_quests), ("dialogue", hand_dialogues)):
        for path in sorted((root / "data" / sub).glob("*.tres")):
            rel = f"data/{sub}/{path.name}"
            text = _read(path)
            if emit.is_generated(text):
                if rel not in r.files and not only:
                    r.errors.append(f"{rel}: generated file with no spec behind it (orphan); delete it or restore its spec")
                continue
            try:
                ident = tres.resource_of(tres.parse(text)).props.get("Id", "")
            except ValueError:
                continue
            bucket[ident] = rel
            if rel in r.files:
                r.errors.append(f"{rel}: is hand-authored; a spec may only patch it, not regenerate it")

    # Patches (applied to the current disk text, or to an earlier patch's result).
    for p in lowered_p:
        rel = hand_quests.get(p.quest_id) or target_of.get(p.quest_id)
        if rel is None:
            r.errors.append(f"patch: no quest file for '{p.quest_id}'")
            continue
        if rel in r.files and rel in target_of.values():
            r.errors.append(f"patch: '{p.quest_id}' is generated; edit its spec instead")
            continue
        text = r.files.get(rel) or _read(root / rel)
        try:
            r.files[rel] = tres.apply_patch(text, p)
        except tres.PatchError as exc:
            r.errors.append(f"patch: {exc}")
            continue
        r.patched.add(rel)

    # Locale blocks.
    csv_path = root / LOCALE
    csv_text = _read(csv_path)
    for tag in c.tags:
        try:
            csv_text = locale.rewrite_block(csv_text, tag, rows_by_tag[tag])
        except ValueError as exc:
            r.errors.append(str(exc))
    r.files[LOCALE] = csv_text

    code = dict(config.CODE_FLAGS)
    code.update(c.code_flags)
    terminal = dict(config.TERMINAL_FLAGS)
    terminal.update(c.terminal_flags)
    overrides = {k: v for k, v in r.files.items() if k.startswith(("data/quests/", "data/dialogue/"))}
    g = graph.build(root, overrides, r.patched, code, terminal, config.ENTRY_QUESTS)
    r.files[GRAPH] = graph.dumps(g)

    errs, warns = validate.validate(root, c, lowered_q, lowered_d, lowered_p, g, csv_text,
                                    hand_quests, hand_dialogues, target_of)
    r.errors += errs
    r.warnings += warns
    r.summary = [f"{len(lowered_q)} quests, {len(lowered_p)} patches, {len(lowered_d)} dialogues, "
                 f"{sum(len(v) for v in rows_by_tag.values())} locale rows from {len(c.modules)} spec(s): "
                 + ", ".join(c.modules)]
    return r


def drift(r: Result, root: Path = ROOT) -> List[str]:
    out = []
    for rel, text in sorted(r.files.items()):
        path = root / rel
        if not path.exists():
            out.append(f"{rel}: missing on disk")
        elif rel == LOCALE:
            if _read(path) != text:
                out.append(f"{rel}: block(s) out of date")
        elif _norm(_read(path)) != _norm(text):
            out.append(f"{rel}: differs from the generated output")
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true", help="diff against disk; exit 1 on drift or validation errors")
    ap.add_argument("--graph-only", action="store_true", help="write only data/story/campaign_graph.json")
    ap.add_argument("--list", action="store_true", help="list the ids each spec produces")
    ap.add_argument("--only", nargs="*", help="restrict to these spec module names (debugging; skips the orphan check)")
    args = ap.parse_args(argv)

    r = compute(ROOT, args.only)
    if args.list:
        c = r.campaign
        for q in c.quests:
            print(f"quest     {q.id:48s} {c.source[q.id]}")
        for p in c.patches:
            print(f"patch     {p.quest_id:48s} {c.source['patch:' + p.quest_id]}")
        for d in c.dialogues:
            print(f"dialogue  {d.id:48s} {c.source[d.id]}")
        return 0
    for w in r.warnings:
        print(f"warning: {w}")
    for e in r.errors:
        print(f"ERROR: {e}")
    print("gen_campaign: " + "; ".join(r.summary))
    if r.errors:
        print(f"gen_campaign: {len(r.errors)} error(s); nothing written" if not args.check
              else f"gen_campaign: {len(r.errors)} error(s)")
        return 1
    if args.check:
        d = drift(r)
        for line in d:
            print(f"DRIFT: {line}")
        if d:
            print("gen_campaign: drift; run python tools/gen_campaign.py")
            return 1
        print("gen_campaign: up to date")
        return 0
    if args.graph_only:
        _write(ROOT / GRAPH, r.files[GRAPH])
        print(f"wrote {GRAPH}")
        return 0
    changed = 0
    for rel, text in sorted(r.files.items()):
        path = ROOT / rel
        if path.exists() and (_read(path) == text or (rel != LOCALE and _norm(_read(path)) == _norm(text))):
            continue
        _write(path, text)
        changed += 1
        print(f"wrote {rel}")
    print(f"gen_campaign: {changed} file(s) changed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
