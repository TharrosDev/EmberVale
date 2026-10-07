"""Stale-binary guard: nothing that launches the game recompiles C#, so the SDK checks first."""
from __future__ import annotations

from pathlib import Path

from quality_common import ROOT

# Where `dotnet build Embervale.sln` puts the assembly the engine loads when run from the project.
ASSEMBLY = ".godot/mono/temp/bin/Debug/Embervale.dll"


def stale_reason(root: Path = ROOT) -> str | None:
    """Why the game assembly must be rebuilt before an engine launch, or None when it is current.

    Stale means the assembly is missing, or Embervale.csproj or any src/**/*.cs was written after it.
    A folder with no src/ is not a checkout this can judge, so it is never stale.
    """
    root = Path(root)
    source = root / "src"
    if not source.is_dir():
        return None
    assembly = root / ASSEMBLY
    if not assembly.is_file():
        return f"{ASSEMBLY} has not been built"
    built = assembly.stat().st_mtime_ns
    project = root / "Embervale.csproj"
    newest, newest_time = None, built
    if project.is_file() and project.stat().st_mtime_ns > newest_time:
        newest, newest_time = project, project.stat().st_mtime_ns
    for path in source.rglob("*.cs"):
        modified = path.stat().st_mtime_ns
        if modified > newest_time:
            newest, newest_time = path, modified
    if newest is None:
        return None
    return f"{newest.relative_to(root).as_posix()} is newer than {ASSEMBLY}"
