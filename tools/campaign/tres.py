"""Minimal .tres reader and the quest patcher.

The reader understands exactly what this repo's quest and dialogue files use: section headers,
`Key = value` lines (strings, numbers, bools, arrays of SubResource refs) and `;` comments.
The patcher edits a hand-authored quest file in place: it appends objective sub-resources, appends
refs to the Objectives array, and fills empty quest fields. It never reorders or rewrites an
existing line other than a flag/field that was empty (and the load_steps count, when present).
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Dict, List

from .emit import objective_lines, q
from .model import Patch


class PatchError(Exception):
    pass


@dataclass
class Section:
    kind: str                       # gd_resource | ext_resource | sub_resource | resource
    attrs: Dict[str, str] = field(default_factory=dict)
    props: Dict[str, object] = field(default_factory=dict)


_HEADER = re.compile(r"^\[(\w+)\s*(.*)\]\s*$")
_ATTR = re.compile(r'(\w+)=("(?:[^"\\]|\\.)*"|[^\s\]]+)')
_REF = re.compile(r'(?:SubResource|ExtResource)\("([^"]+)"\)')


def _unquote(s: str) -> str:
    return re.sub(r'\\(.)', lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), s[1:-1])


def parse_value(raw: str):
    raw = raw.strip()
    if raw.startswith('"') and raw.endswith('"') and len(raw) >= 2:
        return _unquote(raw)
    if raw in ("true", "false"):
        return raw == "true"
    if re.fullmatch(r"-?\d+", raw):
        return int(raw)
    if re.fullmatch(r"-?\d*\.\d+(e-?\d+)?", raw):
        return float(raw)
    if raw.startswith("Array[") or raw.startswith("["):
        return _REF.findall(raw)
    m = re.fullmatch(r'(?:SubResource|ExtResource)\("([^"]+)"\)', raw)
    if m:
        return m.group(1)
    return raw


def _string_complete(raw: str) -> bool:
    if not raw.startswith('"'):
        return True
    i, n = 1, len(raw)
    while i < n:
        if raw[i] == "\\":
            i += 2
            continue
        if raw[i] == '"':
            return True
        i += 1
    return False


def parse(text: str) -> List[Section]:
    sections: List[Section] = []
    cur = None
    lines = text.replace("\r\n", "\n").split("\n")
    i = 0
    while i < len(lines):
        line = lines[i]
        i += 1
        if not line.strip() or line.lstrip().startswith(";"):
            continue
        m = _HEADER.match(line)
        if m:
            cur = Section(m.group(1), {k: (_unquote(v) if v.startswith('"') else v)
                                       for k, v in _ATTR.findall(m.group(2))})
            sections.append(cur)
            continue
        if cur is None or " = " not in line:
            continue
        key, raw = line.split(" = ", 1)
        while not _string_complete(raw.strip()) and i < len(lines):
            raw += "\n" + lines[i]
            i += 1
        cur.props[key.strip()] = parse_value(raw)
    return sections


def resource_of(sections: List[Section]) -> Section:
    for s in sections:
        if s.kind == "resource":
            return s
    raise ValueError("no [resource] section")


def sub_index(sections: List[Section]) -> Dict[str, Section]:
    return {s.attrs["id"]: s for s in sections if s.kind == "sub_resource"}


def ext_paths(sections: List[Section]) -> Dict[str, str]:
    return {s.attrs["id"]: s.attrs.get("path", "") for s in sections if s.kind == "ext_resource"}


# --- patcher ------------------------------------------------------------------------------------

_FLAG_FIELDS = (("auto_start", "AutoStartFlagId"), ("completion_flag", "CompletionFlagId"),
                ("start_flag", "StartFlagId"), ("fail_flag", "FailFlagId"))


def _fmt(value) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return str(value)
    return q(str(value))


def apply_patch(text: str, patch: Patch) -> str:
    """Return `text` patched. `patch` must be lowered (objective text fields are keys).
    Idempotent: applying it to an already patched file returns the same text."""
    crlf = "\r\n" in text
    lines = text.replace("\r\n", "\n").split("\n")
    sections = parse("\n".join(lines))
    res = resource_of(sections)
    if res.props.get("Id") != patch.quest_id:
        raise PatchError(f"file holds quest {res.props.get('Id')!r}, not {patch.quest_id!r}")
    r = next(i for i, ln in enumerate(lines) if ln.strip() == "[resource]")

    # 1) appended objectives
    existing = sub_index(sections)
    obj_ext = next((k for k, v in ext_paths(sections).items() if v.endswith("/ObjectiveResource.cs")), None)
    if patch.append and obj_ext is None:
        raise PatchError(f"{patch.quest_id}: file has no ObjectiveResource ext_resource")
    new_blocks: List[List[str]] = []
    new_refs: List[str] = []
    for i, o in enumerate(patch.append):
        sid = o.sub_id or f"Obj_{o.tag or i + 1}"
        new_refs.append(sid)
        if sid in existing:
            continue
        new_blocks.append(objective_lines(o, sid, obj_ext) + [""])
    if new_blocks:
        flat = [ln for b in new_blocks for ln in b]
        lines[r:r] = flat
        r += len(flat)
    if new_refs:
        for i in range(r, len(lines)):
            m = re.fullmatch(r"Objectives = Array\[Resource\]\(\[(.*)\]\)", lines[i])
            if m:
                refs = _REF.findall(m.group(1))
                add = [s for s in new_refs if s not in refs]
                if add:
                    allrefs = [f'SubResource("{s}")' for s in refs + add]
                    lines[i] = f"Objectives = Array[Resource]([{', '.join(allrefs)}])"
                break
        else:
            raise PatchError(f"{patch.quest_id}: no Objectives line")

    # 2) fields
    wanted = [(f, getattr(patch, a)) for a, f in _FLAG_FIELDS if getattr(patch, a) is not None]
    wanted += list(patch.fields.items())
    for name, value in wanted:
        for i in range(r, len(lines)):
            if lines[i].startswith(f"{name} = "):
                cur = parse_value(lines[i].split(" = ", 1)[1])
                if cur == value:
                    break
                if cur in ("", 0, False, 0.0):
                    lines[i] = f"{name} = {_fmt(value)}"
                    break
                raise PatchError(f"{patch.quest_id}: {name} is already {cur!r}; refusing to change it to {value!r}")
        else:
            end = len(lines)
            while end > r and not lines[end - 1].strip():
                end -= 1
            lines.insert(end, f"{name} = {_fmt(value)}")

    # 3) load_steps, when the header carries it
    if new_blocks and "load_steps=" in lines[0]:
        lines[0] = re.sub(r"load_steps=(\d+)", lambda m: f"load_steps={int(m.group(1)) + len(new_blocks)}", lines[0])

    out = "\n".join(lines)
    if not out.endswith("\n"):
        out += "\n"
    return out.replace("\n", "\r\n") if crlf else out
