#!/usr/bin/env python3
"""Diagnose Embervale's local-only Godot MCP before screenshot work.

This never starts the editor/server and never falls back to the vendor cloud. It proves the
project config is loopback-only, the CLI exists, status responds, and optionally invokes a real
editor tool so a listening relay with no editor cannot report green.

    python tools/godot_mcp_check.py [--probe] [--json]

`check()` is the same diagnosis as data; `embervale.py doctor` reports it as one row.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from urllib.parse import urlparse

from quality_common import ROOT, command_text, run_process


def check(probe: bool = False, timeout: float = 30) -> dict:
    """{"status": pass|fail|blocked, "exit_code": 0|1|2, "url": ..., "detail": one line,
    "output": raw CLI text}. blocked means it could not be checked (no config, no CLI)."""
    config_path = ROOT / ".mcp.json"
    try:
        config = json.loads(config_path.read_text(encoding="utf-8"))
        url = config["mcpServers"]["ai-game-developer"]["url"]
    except (OSError, ValueError, KeyError, TypeError) as error:
        return dict(status="blocked", exit_code=2, url=None, output="", detail=f"config: {config_path}: {error}")
    parsed = urlparse(url)
    if parsed.scheme not in ("http", "https") or parsed.hostname not in ("localhost", "127.0.0.1", "::1"):
        return dict(status="fail", exit_code=1, url=url, output="", detail=f"privacy: MCP URL is not loopback-only: {url}")
    cli = shutil.which("godot-cli")
    if not cli:
        return dict(status="blocked", exit_code=2, url=url, output="",
                    detail="godot-cli is not on PATH. Install/use the vendored Godot-MCP CLI; do not switch to cloud mode.")
    # An isolated worktree hashes to a different default port. Use the configured relay
    # for both status and the tool call instead of probing two unrelated endpoints.
    base_url = f"{parsed.scheme}://{parsed.hostname}:{parsed.port or 23630}"
    status = run_process([cli, "status", ".", "--url", base_url], cwd=ROOT, timeout=timeout)
    output = status.output
    if status.timed_out or status.launch_error or status.returncode:
        return dict(status="fail", exit_code=1, url=base_url, output=output,
                    detail="local MCP status did not prove both editor and relay ready. "
                           "Start them in Custom mode as documented in CLAUDE.md.")
    if probe:
        command = [cli, "run-tool", "scene-list-opened", ".", "--url", base_url, "--input", "{}"]
        result = run_process(command, cwd=ROOT, timeout=45)
        output += result.output
        if result.timed_out or result.launch_error or result.returncode:
            return dict(status="fail", exit_code=1, url=base_url, output=output,
                        detail=f"editor probe. Reproduce: {command_text(command)}")
    return dict(status="pass", exit_code=0, url=base_url, output=output,
                detail="Godot MCP is local-only and responsive. Screenshot tools may now be used.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--probe", action="store_true",
                        help="invoke scene-list-opened after status (requires a running editor)")
    parser.add_argument("--json", action="store_true", help="one JSON line: status, exit_code, url, detail")
    args = parser.parse_args()
    result = check(args.probe)
    if args.json:
        print(json.dumps({k: v for k, v in result.items() if k != "output"}))
        return result["exit_code"]
    print(result["output"], end="")
    label = {"pass": "PASS", "fail": "FAIL", "blocked": "BLOCKED"}[result["status"]]
    print(f"{label}: {result['detail']}", file=sys.stdout if result["exit_code"] == 0 else sys.stderr)
    return result["exit_code"]


if __name__ == "__main__":
    raise SystemExit(main())
