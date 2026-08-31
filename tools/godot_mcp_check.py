#!/usr/bin/env python3
"""Diagnose Embervale's local-only Godot MCP before screenshot work.

This never starts the editor/server and never falls back to the vendor cloud. It proves the
project config is loopback-only, the CLI exists, status responds, and optionally invokes a real
editor tool so a listening relay with no editor cannot report green.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path
from urllib.parse import urlparse

from quality_common import ROOT, command_text, run_process


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--probe", action="store_true",
                        help="invoke scene-list-opened after status (requires a running editor)")
    args = parser.parse_args()
    config_path = ROOT / ".mcp.json"
    try:
        config = json.loads(config_path.read_text(encoding="utf-8"))
        url = config["mcpServers"]["ai-game-developer"]["url"]
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"FAIL config: {config_path}: {error}", file=sys.stderr)
        return 2
    parsed = urlparse(url)
    if parsed.scheme not in ("http", "https") or parsed.hostname not in ("localhost", "127.0.0.1", "::1"):
        print(f"FAIL privacy: MCP URL is not loopback-only: {url}", file=sys.stderr)
        return 1
    cli = shutil.which("godot-cli")
    if not cli:
        print("BLOCKED: godot-cli is not on PATH. Install/use the vendored Godot-MCP CLI; "
              "do not switch to cloud mode.", file=sys.stderr)
        return 2
    status = run_process([cli, "status", "."], cwd=ROOT, timeout=30)
    print(status.output, end="")
    if status.timed_out or status.launch_error or status.returncode:
        print("FAIL: local MCP status did not prove both editor and relay ready. "
              "Start them in Custom mode as documented in CLAUDE.md.", file=sys.stderr)
        return 1
    if args.probe:
        command = [cli, "run-tool", "scene-list-opened", ".", "--url",
                   f"{parsed.scheme}://{parsed.hostname}:{parsed.port or 23630}", "--input", "{}"]
        probe = run_process(command, cwd=ROOT, timeout=45)
        print(probe.output, end="")
        if probe.timed_out or probe.launch_error or probe.returncode:
            print(f"FAIL editor probe. Reproduce: {command_text(command)}", file=sys.stderr)
            return 1
    print("PASS: Godot MCP is local-only and responsive. Screenshot tools may now be used.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
