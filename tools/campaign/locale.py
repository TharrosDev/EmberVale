"""Locale: derive keys from prose, and rewrite per-workstream blocks of data/locale/strings.csv.

Block markers (pre-inserted by the playbook):
    # --- BEGIN campaign:<tag> ---
    # --- END campaign:<tag> ---
Only the text between the markers of the spec's own tag is ever rewritten. A block missing from the
file is appended at the end (only when the tag has rows).
"""

from __future__ import annotations

import copy
import csv
import io
import re
from typing import Dict, Iterable, List, Tuple

from .model import K, Dialogue, Patch, Quest

Rows = List[Tuple[str, str]]
PALE_PHRASE = "pale concord"
KEY_RE = re.compile(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")


def looks_like_key(text: str) -> bool:
    return bool(KEY_RE.match(text))


# --- key derivation ------------------------------------------------------------------------------

def _tail(ident: str) -> str:
    return ident.split(".")[-1]


def quest_prefix(q: Quest) -> str:
    return f"{q.secret}.quest.{_tail(q.id)}" if q.secret else q.id


def dialogue_prefix(d: Dialogue) -> str:
    stem = d.stem or (d.id[len("dialogue."):] if d.id.startswith("dialogue.") else d.id)
    return f"{d.secret}.dlg.{stem}" if d.secret else f"dlg.{stem}"


class _Lowerer:
    def __init__(self) -> None:
        self.rows: Rows = []

    def text(self, value: str, key: str) -> str:
        """Prose -> key (+ row); K -> itself; empty -> empty."""
        if not value:
            return ""
        if isinstance(value, K):
            return str(value)
        if "\n" in value or "\r" in value:
            raise ValueError(f"locale text for '{key}' contains a newline (strings.csv rows are one physical line)")
        self.rows.append((key, value))
        return key

    def objective(self, o, prefix: str, index: int):
        tag = o.tag or str(index + 1)
        o.text = self.text(o.text, f"{prefix}.obj_{tag}")
        o.hint = self.text(o.hint, f"{prefix}.hint_{tag}")
        o.journal = self.text(o.journal, f"{prefix}.log_{tag}")

    def quest(self, q: Quest, default_secret) -> Quest:
        q = copy.deepcopy(q)
        if q.secret is None:
            q.secret = default_secret
        p = quest_prefix(q)
        q.title = self.text(q.title, f"{p}.title")
        q.summary = self.text(q.summary, f"{p}.summary")
        q.detail = self.text(q.detail, f"{p}.detail")
        for i, o in enumerate(q.objectives):
            self.objective(o, p, i)
        return q

    def patch(self, pt: Patch) -> Patch:
        pt = copy.deepcopy(pt)
        for i, o in enumerate(pt.append):
            if not o.tag and not isinstance(o.text, K):
                raise ValueError(f"patch of {pt.quest_id}: appended objective {i} needs a tag")
            self.objective(o, pt.quest_id, i)
        return pt

    def dialogue(self, d: Dialogue, default_secret) -> Dialogue:
        d = copy.deepcopy(d)
        if d.secret is None:
            d.secret = default_secret
        p = dialogue_prefix(d)
        d.speaker = self.text(d.speaker, f"{p}.speaker")
        for n in d.nodes:
            n.text = self.text(n.text, f"{p}.{n.id}")
            n.speaker = self.text(n.speaker, f"{p}.{n.id}.speaker")
            for j, c in enumerate(n.choices):
                c.text = self.text(c.text, f"{p}.{n.id}.c_{c.tag or j}")
        return d


def lower(quests: List[Quest], patches: List[Patch], dialogues: List[Dialogue], default_secret=None):
    """Resolve every prose field to a key. Returns (quests, patches, dialogues, rows)."""
    lw = _Lowerer()
    rq = [lw.quest(q, default_secret) for q in quests]
    rp = [lw.patch(p) for p in patches]
    rd = [lw.dialogue(d, default_secret) for d in dialogues]
    return rq, rp, rd, lw.rows


# --- csv -----------------------------------------------------------------------------------------

def fmt_row(key: str, value: str) -> str:
    """Same quoting as the existing rows: quote only when the value needs it."""
    if "\n" in value or "\r" in value:
        raise ValueError(f"value for '{key}' contains a newline")
    if any(ch in value for ch in ',"') or value != value.strip():
        value = '"' + value.replace('"', '""') + '"'
    return f"{key},{value}"


def eol_of(text: str) -> str:
    return "\r\n" if "\r\n" in text else "\n"


def parse_rows(text: str) -> List[List[str]]:
    """Data rows (comments and blanks skipped), RFC-4180 aware."""
    out = []
    for row in csv.reader(io.StringIO(text, newline="")):
        if not row or not row[0].strip() or row[0].lstrip().startswith("#"):
            continue
        out.append(row)
    return out


def catalog_keys(text: str) -> set:
    return {r[0] for r in parse_rows(text) if r[0] != "keys"}


def markers(tag: str) -> Tuple[str, str]:
    return f"# --- BEGIN campaign:{tag} ---", f"# --- END campaign:{tag} ---"


def rewrite_block(text: str, tag: str, rows: Rows) -> str:
    """Return `text` with only the `campaign:<tag>` block replaced (idempotent)."""
    eol = eol_of(text)
    begin, end = markers(tag)
    lines = text.split(eol)
    body = [fmt_row(k, v) for k, v in rows]
    try:
        b = lines.index(begin)
        e = lines.index(end, b + 1)
    except ValueError:
        if not rows:
            return text
        trailing = lines[-1] == ""
        base = lines[:-1] if trailing else lines
        return eol.join(base + [begin] + body + [end] + [""])
    return eol.join(lines[:b + 1] + body + lines[e:])


def block_rows(text: str, tag: str) -> List[str]:
    begin, end = markers(tag)
    lines = text.split(eol_of(text))
    if begin not in lines or end not in lines:
        return []
    return lines[lines.index(begin) + 1:lines.index(end)]


def audit(text: str) -> List[str]:
    """File-wide rules: no duplicate key anywhere; the Pale Concord is named only under pale.* keys."""
    errors: List[str] = []
    seen: Dict[str, int] = {}
    for n, line in enumerate(text.replace("\r", "").split("\n"), 1):
        s = line.lstrip()
        if not s or s.startswith("#"):
            continue
        key = line.split(",", 1)[0].strip()
        if key == "keys":
            continue
        if key in seen:
            errors.append(f"locale: duplicate key '{key}' (lines {seen[key]} and {n})")
        else:
            seen[key] = n
    for row in parse_rows(text):
        if len(row) > 1 and not row[0].startswith("pale.") and any(PALE_PHRASE in c.lower() for c in row[1:]):
            errors.append(f"locale: secret leak: '{row[0]}' names the Pale Concord outside a pale.* key")
    return errors
