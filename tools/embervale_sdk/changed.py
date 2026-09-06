"""Git selects candidates; Godot builds the transitive dependency closure."""
from quality_common import ROOT, run_process


def changed_paths(base=None, root=ROOT):
    commands = [["git", "diff", "--name-only", "-z", "HEAD"],
                ["git", "ls-files", "--others", "--exclude-standard", "-z"]]
    if base:
        if base.startswith("-"):
            raise ValueError("base must be a Git revision, not an option")
        commands.append(["git", "diff", "--name-only", "-z", f"{base}...HEAD"])
    paths = set()
    for command in commands:
        result = run_process(command, timeout=30, cwd=root)
        if result.returncode:
            raise ValueError(f"cannot determine changed files: {result.output or result.launch_error}")
        paths.update(p.replace("\\", "/") for p in result.stdout.split("\0") if p)
    # C# and project configuration can affect all resources; never pretend they are local.
    full = any(p.endswith((".cs", ".csproj", ".sln")) or p == "project.godot" or
               p.startswith(("tools/", "addons/")) for p in paths)
    return sorted(paths), full
