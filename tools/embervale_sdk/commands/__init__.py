"""SDK subcommand registry.

Every `python tools/embervale.py <command>` is a Command in REGISTRY. The original commands are
registered by cli.py; a NEW one is a single module in this folder and nothing else: it is found by
name, so there is no list to edit.

    # tools/embervale_sdk/commands/my_thing.py   ->   python tools/embervale.py my-thing --depth 2
    HELP = "One line for `list` and --help."

    def arguments(parser):            # optional: flags only this command accepts
        parser.add_argument("--depth", type=int, default=1)

    def run(run, args, passthrough):  # run is the cli.Run for this invocation
        run.godot("my-thing", [], ["--state"])            # engine step (owns timeout, logs, stale build)
        run.process("my-step", ["python", "tools/x.py"])  # any other process step
        run.result["metrics"]["my_thing"] = {"depth": args.depth}
        # run.issue(code, message) records a diagnostic; raise ValueError for a usage error (exit 2).

Optional module attributes: NAME (default: the file name with `_` as `-`), VERSION = False to skip
the `godot --version` step for a command that never launches the engine, PASSTHROUGH = True to accept
arguments after `--`. A command's own flags go AFTER the command name; the shared flags (--json,
--timeout, --artifacts, ...) work everywhere, and a flag that collides with one of them fails at
startup rather than shadowing it. Modules whose name starts with `_` are not commands.

LIGHT = True is for a command that launches nothing and wants no run directory (`last`, `logs`,
`job`, `clean`): its run() is called with run=None, prints its own output and returns its exit code.
A light command that sometimes does need a full run calls cli.run_command(args, passthrough, entry,
body) itself, as `verify` does after `--plan`.
"""
from __future__ import annotations

import importlib
import pkgutil
from dataclasses import dataclass
from typing import Callable, Optional


@dataclass(frozen=True)
class Command:
    name: str
    run: Callable                         # run(run, args, passthrough)
    help: str = ""
    arguments: Optional[Callable] = None  # arguments(parser) adds this command's own flags
    version: bool = True                  # record `godot --version` before running
    passthrough: bool = False             # accepts arguments after `--`
    light: bool = False                   # no Run, no run directory: run(None, args, passthrough) -> exit code


REGISTRY: dict[str, Command] = {}


def register(name, run, help="", arguments=None, version=True, passthrough=False, light=False):
    if name in REGISTRY:
        raise ValueError(f"duplicate SDK command: {name}")
    REGISTRY[name] = Command(name, run, help, arguments, version, passthrough, light)
    return REGISTRY[name]


def register_module(module):
    """Registers one command module (see the module docstring for its shape)."""
    if not callable(getattr(module, "run", None)):
        raise ValueError(f"{module.__name__} is not an SDK command: it has no run(run, args, passthrough)")
    name = getattr(module, "NAME", module.__name__.rsplit(".", 1)[-1].replace("_", "-"))
    return register(name, module.run, getattr(module, "HELP", (module.__doc__ or "").strip().split("\n")[0]),
                    getattr(module, "arguments", None), getattr(module, "VERSION", True),
                    getattr(module, "PASSTHROUGH", False), getattr(module, "LIGHT", False))


def discover():
    """Imports and registers every command module in this package, in name order. Idempotent."""
    for info in sorted(pkgutil.iter_modules(__path__), key=lambda m: m.name):
        module_name = f"{__name__}.{info.name}"
        if info.name.startswith("_") or any(c.run.__module__ == module_name for c in REGISTRY.values()):
            continue
        register_module(importlib.import_module(module_name))
