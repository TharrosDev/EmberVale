"""Spec discovery. Every tools/campaign/specs/<name>.py (not starting with '_') exposes

    build() -> (items, dialogues, locale_rows, tag)

`items` is a list of model.Quest and model.Patch, `dialogues` a list of model.Dialogue,
`locale_rows` a list of (key, text) for strings the spec writes by hand (everything written as prose
on a model object is derived automatically), `tag` the locale block ('legacy', 'spec1', ...).
Optional module attributes: SECRET = 'pale' (default for every object of the module),
CODE_FLAGS = {flag: writer note}, TERMINAL_FLAGS = {flag: reason}.
"""

from __future__ import annotations

import importlib
import pkgutil
from dataclasses import dataclass, field
from typing import Dict, List, Tuple

from . import specs
from .model import Dialogue, Patch, Quest


@dataclass
class Campaign:
    quests: List[Quest] = field(default_factory=list)
    patches: List[Patch] = field(default_factory=list)
    dialogues: List[Dialogue] = field(default_factory=list)
    manual_rows: Dict[str, List[Tuple[str, str]]] = field(default_factory=dict)   # tag -> rows
    source: Dict[str, str] = field(default_factory=dict)       # object id -> spec path
    tag_of: Dict[str, str] = field(default_factory=dict)       # object id (patch:<id> for patches) -> locale tag
    code_flags: Dict[str, str] = field(default_factory=dict)
    terminal_flags: Dict[str, str] = field(default_factory=dict)
    tags: List[str] = field(default_factory=list)              # tags in discovery order (all specs)
    modules: List[str] = field(default_factory=list)


def discover(only: List[str] = None, extra_modules=()) -> Campaign:
    """Import every spec module. `extra_modules` (already imported, e.g. in tests) are appended."""
    c = Campaign()
    names = sorted(m.name for m in pkgutil.iter_modules(specs.__path__) if not m.name.startswith("_"))
    mods = [(name, importlib.import_module(f"{specs.__name__}.{name}")) for name in names
            if only is None or name in only]
    mods += [(m.__name__.rsplit(".", 1)[-1], m) for m in extra_modules]
    for name, mod in mods:
        items, dialogues, rows, tag = mod.build()
        path = f"tools/campaign/specs/{name}.py"
        default_secret = getattr(mod, "SECRET", None)
        c.modules.append(name)
        for it in items:
            if isinstance(it, Patch):
                c.patches.append(it)
                c.source[f"patch:{it.quest_id}"] = path
                c.tag_of[f"patch:{it.quest_id}"] = tag
            elif isinstance(it, Quest):
                c.quests.append(it)
                c.source[it.id] = path
                c.tag_of[it.id] = tag
                if default_secret and it.secret is None:
                    it.secret = default_secret
            else:
                raise TypeError(f"{path}: build() returned {type(it).__name__} in its quest list")
        for d in dialogues:
            c.dialogues.append(d)
            c.source[d.id] = path
            c.tag_of[d.id] = tag
            if default_secret and d.secret is None:
                d.secret = default_secret
        c.manual_rows.setdefault(tag, []).extend(rows)
        if tag not in c.tags:
            c.tags.append(tag)
        c.code_flags.update(getattr(mod, "CODE_FLAGS", {}))
        c.terminal_flags.update(getattr(mod, "TERMINAL_FLAGS", {}))
    return c
