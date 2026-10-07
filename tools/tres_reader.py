#!/usr/bin/env python3
"""The one shared, read-only reader for Godot text resources (.tres) and scenes (.tscn).

    doc = tres_reader.load(path)          # or parse(text)
    doc.root                              # the [resource] section (None for a scene)
    doc.root.get("Health", 0.0)           # typed value: str, int, float, bool, list, Ref, Raw
    doc.subs["Entry_scrap"]               # a [sub_resource] by id
    doc.ext["2_wpn"]                      # an ext_resource id -> its res:// path
    doc.resolve(value)                    # Ref -> Section (sub) or res:// path (ext); lists element-wise
    doc.nodes                             # [node] sections of a scene, each with .path
    for section, key, text in doc.strings(): ...   # every quoted string in the file

It understands what this repo's data uses: section headers with attributes, `Key = value` lines,
multi-line strings and arrays, `;` comments. It never writes. Engine value types it has no use for
(Vector3, Color, Transform3D, dictionaries) come back as `Raw`, a str subclass holding the source
text, so a caller can still regex inside them. tools/campaign/tres.py keeps its own small reader
because its patcher edits lines in place; new read-only code uses this module.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterator


class Raw(str):
    """Source text of a value this reader does not interpret."""


@dataclass(frozen=True)
class Ref:
    kind: str  # "sub" | "ext"
    id: str


@dataclass
class Section:
    kind: str  # gd_resource | gd_scene | ext_resource | sub_resource | resource | node | connection
    attrs: dict[str, object] = field(default_factory=dict)
    props: dict[str, object] = field(default_factory=dict)
    line: int = 0

    def get(self, key: str, default=None):
        return self.props.get(key, default)

    @property
    def label(self) -> str:
        """A short name for reports: the sub-resource id, the node path, or the section kind."""
        if self.kind == "sub_resource":
            return str(self.attrs.get("id", "?"))
        if self.kind == "node":
            return self.path
        return self.kind

    @property
    def path(self) -> str:
        """Scene-tree path of a [node] section, relative to the scene root ('.' for the root)."""
        name = str(self.attrs.get("name", ""))
        parent = self.attrs.get("parent")
        if parent is None:
            return "."
        return name if parent == "." else f"{parent}/{name}"


_HEADER = re.compile(r"^\[(\w+)\s*(.*)\]\s*$")
_ATTR = re.compile(r'(\w+)=("(?:[^"\\]|\\.)*"|(?:Sub|Ext)Resource\("[^"]*"\)|\[[^\]]*\]|[^\s\]]+)')
_REF = re.compile(r'(Sub|Ext)Resource\(\s*"([^"]*)"\s*\)')
_INT = re.compile(r"-?\d+")
_FLOAT = re.compile(r"-?(?:\d+\.\d*|\.\d+|\d+)(?:e[-+]?\d+)?|-?inf|nan")
_QUOTED = re.compile(r'"((?:[^"\\]|\\.)*)"')
_ESCAPES = {"n": "\n", "t": "\t", "r": "\r"}


def unquote(text: str) -> str:
    return re.sub(r"\\(.)", lambda m: _ESCAPES.get(m.group(1), m.group(1)), text[1:-1])


def _split_top(text: str) -> list[str]:
    """Split on commas that are outside quotes and brackets."""
    parts: list[str] = []
    depth = 0
    start = 0
    quoted = False
    i = 0
    while i < len(text):
        ch = text[i]
        if quoted:
            if ch == "\\":
                i += 1
            elif ch == '"':
                quoted = False
        elif ch == '"':
            quoted = True
        elif ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "," and depth == 0:
            parts.append(text[start:i])
            start = i + 1
        i += 1
    tail = text[start:]
    if tail.strip():
        parts.append(tail)
    return [part.strip() for part in parts]


def parse_value(raw: str):
    raw = raw.strip()
    if len(raw) >= 2 and raw[0] == '"' and raw[-1] == '"' and _QUOTED.fullmatch(raw):
        return unquote(raw)
    if raw.startswith('&"') and raw.endswith('"'):
        return unquote(raw[1:])
    if raw in ("true", "false"):
        return raw == "true"
    if raw == "null":
        return None
    if _INT.fullmatch(raw):
        return int(raw)
    if _FLOAT.fullmatch(raw):
        return float(raw)
    ref = _REF.fullmatch(raw)
    if ref:
        return Ref("sub" if ref.group(1) == "Sub" else "ext", ref.group(2))
    if raw.startswith("Array[") and raw.endswith(")"):
        # Array[Type]([a, b]) - the type itself may be ExtResource("id"), so find the last "([".
        opening = raw.find("]([")
        if opening >= 0 and raw.endswith("])"):
            return [parse_value(part) for part in _split_top(raw[opening + 3:-2])]
    if raw.startswith("[") and raw.endswith("]"):
        return [parse_value(part) for part in _split_top(raw[1:-1])]
    packed = re.fullmatch(r"Packed(?:Int32|Int64|Float32|Float64|String)Array\((.*)\)", raw, re.DOTALL)
    if packed:
        return [parse_value(part) for part in _split_top(packed.group(1))]
    return Raw(raw)


def _complete(raw: str) -> bool:
    """True when quotes and brackets are balanced, so the value does not continue on the next line."""
    depth = 0
    quoted = False
    i = 0
    while i < len(raw):
        ch = raw[i]
        if quoted:
            if ch == "\\":
                i += 1
            elif ch == '"':
                quoted = False
        elif ch == '"':
            quoted = True
        elif ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        i += 1
    return not quoted and depth <= 0


def strings_in(value) -> Iterator[str]:
    """Every quoted string inside a parsed value, including those inside an uninterpreted Raw."""
    if isinstance(value, Raw):
        for match in _QUOTED.finditer(value):
            yield unquote(match.group(0))
    elif isinstance(value, str):
        yield value
    elif isinstance(value, list):
        for item in value:
            yield from strings_in(item)


@dataclass
class Doc:
    sections: list[Section]

    @property
    def header(self) -> Section | None:
        return self.sections[0] if self.sections and self.sections[0].kind.startswith("gd_") else None

    @property
    def root(self) -> Section | None:
        return next((s for s in self.sections if s.kind == "resource"), None)

    @property
    def script_class(self) -> str:
        header = self.header
        return str(header.attrs.get("script_class", "")) if header else ""

    @property
    def subs(self) -> dict[str, Section]:
        return {str(s.attrs.get("id")): s for s in self.sections if s.kind == "sub_resource"}

    @property
    def ext(self) -> dict[str, str]:
        return {str(s.attrs.get("id")): str(s.attrs.get("path", ""))
                for s in self.sections if s.kind == "ext_resource"}

    @property
    def nodes(self) -> list[Section]:
        return [s for s in self.sections if s.kind == "node"]

    def resolve(self, value):
        if isinstance(value, Ref):
            return self.subs.get(value.id) if value.kind == "sub" else self.ext.get(value.id)
        if isinstance(value, list):
            return [self.resolve(item) for item in value]
        return value

    def strings(self) -> Iterator[tuple[Section, str, str]]:
        for section in self.sections:
            for key, value in section.props.items():
                for text in strings_in(value):
                    yield section, key, text


def parse(text: str) -> Doc:
    sections: list[Section] = []
    current: Section | None = None
    lines = text.replace("\r\n", "\n").split("\n")
    i = 0
    while i < len(lines):
        line = lines[i]
        i += 1
        stripped = line.strip()
        if not stripped or stripped.startswith(";"):
            continue
        header = _HEADER.match(line) if line.startswith("[") else None
        if header:
            attrs = {}
            for key, value in _ATTR.findall(header.group(2)):
                attrs[key] = parse_value(value)
            current = Section(header.group(1), attrs, {}, i)
            sections.append(current)
            continue
        if current is None or " = " not in line:
            continue
        key, raw = line.split(" = ", 1)
        while not _complete(raw) and i < len(lines):
            raw += "\n" + lines[i]
            i += 1
        current.props[key.strip()] = parse_value(raw)
    return Doc(sections)


def load(path: Path | str) -> Doc:
    return parse(Path(path).read_text(encoding="utf-8", errors="replace"))
