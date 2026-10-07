"""Stale-binary guard: nothing that launches the game recompiles C#, so the SDK checks first."""
from __future__ import annotations

from pathlib import Path

from quality_common import ROOT

# Where `dotnet build Embervale.sln` puts the assembly the engine loads when run from the project.
ASSEMBLY = ".godot/mono/temp/bin/Debug/Embervale.dll"
# A type that exists only in a tooling build, as it appears in the assembly's metadata strings
# (the same scan tools/check_shipping_assembly.py uses to prove the opposite).
TOOLING_MARKER = b"\x00ShotHarness\x00"
# Folders whose *.cs the tooling build compiles.
SOURCES = ("src", "addons")


def stale_reason(root: Path = ROOT) -> str | None:
    """Why the game assembly must be rebuilt before an engine launch, or None when it is current.

    Stale means the assembly is missing, Embervale.csproj or any src/ or addons/ *.cs was written
    after it, or it was built without tooling. `world_bake.py --bake` leaves that last kind behind
    (`-p:EmbervaleTooling=false`): newer than every source, yet with no harness flags and with
    EMBERVALE_USER_DIR ignored, so a gate run on it would use the player's real saves.
    A folder with no src/ is not a checkout this can judge, so it is never stale.
    """
    root = Path(root)
    if not (root / "src").is_dir():
        return None
    assembly = root / ASSEMBLY
    if not assembly.is_file():
        return f"{ASSEMBLY} has not been built"
    built = assembly.stat().st_mtime_ns
    project = root / "Embervale.csproj"
    newest, newest_time = None, built
    if project.is_file() and project.stat().st_mtime_ns > newest_time:
        newest, newest_time = project, project.stat().st_mtime_ns
    for folder in SOURCES:
        for path in (root / folder).rglob("*.cs"):
            modified = path.stat().st_mtime_ns
            if modified > newest_time:
                newest, newest_time = path, modified
    if newest is not None:
        return f"{newest.relative_to(root).as_posix()} is newer than {ASSEMBLY}"
    try:
        tooling = TOOLING_MARKER in assembly.read_bytes()
    except OSError as error:
        return f"{ASSEMBLY} could not be read ({error})"
    if not tooling:
        return f"{ASSEMBLY} was built without tooling (a world bake leaves it so)"
    return None
