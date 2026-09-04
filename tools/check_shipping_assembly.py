#!/usr/bin/env python3
"""Prove the shipping build carries no development tooling.

The Godot .NET SDK compiles every *.cs under the project into ONE assembly, so the
only separation available is per build configuration (Embervale.csproj -> the
tooling gate). This checks the gate actually held: it reads the ExportRelease
assembly's metadata strings and fails if any dev-only type name is present.

Usage:  python tools/check_shipping_assembly.py   (it builds ExportRelease itself)
"""
import pathlib
import re
import subprocess
import sys

DLL = pathlib.Path(".godot/mono/temp/bin/ExportRelease/Embervale.dll")

# Type names that must not exist in a shipping build.
FORBIDDEN = [
    "ShellShots", "HudShots", "PanelShots", "ShrineShots", "GuildShots",
    "EnemyShots", "ShotHarness", "ReproHarness",
]
# Namespaces from the vendored Godot-MCP addon and its NuGet dependencies.
FORBIDDEN_PREFIXES = ["IvanMurzak", "GodotMCP", "com.IvanMurzak"]


def main() -> int:
    # Self-contained: build the configuration under test rather than trusting whatever a previous
    # step happened to leave behind. A stale DLL would make this gate pass on the wrong assembly.
    build = subprocess.run(
        ["dotnet", "build", "Embervale.csproj", "-c", "ExportRelease", "--nologo", "-v", "q"],
        capture_output=True, text=True)
    if build.returncode != 0:
        print("FAIL: the ExportRelease build did not succeed:")
        print(build.stdout.strip() or build.stderr.strip())
        return 1

    if not DLL.exists():
        print(f"FAIL: {DLL} not found after a successful build.")
        return 1

    blob = DLL.read_bytes()
    # Metadata strings are UTF-8 and null-separated; a plain byte scan is enough
    # to prove absence, which is what this gate asserts.
    found = []
    for name in FORBIDDEN:
        if re.search(rb"\x00" + re.escape(name.encode()) + rb"\x00", blob):
            found.append(name)
    for prefix in FORBIDDEN_PREFIXES:
        if prefix.encode() in blob:
            found.append(prefix + "*")

    also = list(DLL.parent.glob("*IvanMurzak*")) + list(DLL.parent.glob("*McpPlugin*"))
    if also:
        found += [p.name for p in also]

    if found:
        print("FAIL: shipping assembly still contains development tooling:")
        for name in sorted(set(found)):
            print(f"  - {name}")
        return 1

    print(f"PASS: {DLL} ({DLL.stat().st_size // 1024} KiB) carries no dev tooling.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
