#!/usr/bin/env python3
"""Prove the shipping build carries no development tooling.

The Godot .NET SDK compiles every *.cs under the project into ONE assembly, so the
only separation available is per build configuration (Embervale.csproj -> the
tooling gate). This checks the gate actually held: it reads the ExportRelease
assembly's metadata strings and fails if any dev-only type name is present.

Usage:  dotnet build Embervale.csproj -c ExportRelease && python tools/check_shipping_assembly.py
"""
import pathlib
import re
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
    if not DLL.exists():
        print(f"FAIL: {DLL} not found. Run: dotnet build Embervale.csproj -c ExportRelease")
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
