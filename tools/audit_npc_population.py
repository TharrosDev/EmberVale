#!/usr/bin/env python3
"""Audit placed human identities, base-mesh reuse, and modular-profile coverage."""

from __future__ import annotations

import argparse
import csv
import re
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
EXT_RE = re.compile(r'\[ext_resource type="PackedScene" path="res://assets/models/characters/(npc_[^"]+)" id="([^"]+)"\]')
NODE_RE = re.compile(r'^\[node name="([^"]+)" type="Node3D" parent="\."\]$', re.M)
MODEL_RE = re.compile(r'^\[node name="Model" parent="([^"]+)" instance=ExtResource\("([^"]+)"\)\]$', re.M)
PROFILE_RE = re.compile(r'^\s*\["(npc\.[^"]+)"\]\s*=\s*P\("([^"]+)",\s*Build\.([A-Za-z]+),', re.M)


def scene_records(path: Path):
    text = path.read_text(encoding="utf-8")
    resources = {match.group(2): match.group(1) for match in EXT_RE.finditer(text)}
    blocks = list(NODE_RE.finditer(text))
    values = {}
    for index, match in enumerate(blocks):
        end = blocks[index + 1].start() if index + 1 < len(blocks) else len(text)
        block = text[match.start():end]
        display = re.search(r'^DisplayName = "(.*)"$', block, re.M)
        template = re.search(r'^TemplateId = "(.*)"$', block, re.M)
        values[match.group(1)] = {
            "display": display.group(1) if display else "",
            "template": template.group(1) if template else "",
        }
    for match in MODEL_RE.finditer(text):
        parent, resource = match.groups()
        if parent in values and resource in resources:
            yield {
                "scene": path.relative_to(ROOT).as_posix(), "node": parent,
                **values[parent], "base_model": resources[resource],
            }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="reports/3d/session-03-npc-handoff/npc-population-audit")
    args = parser.parse_args()
    output = ROOT / args.output
    output.mkdir(parents=True, exist_ok=True)
    profile_text = (ROOT / "src/Npc/NpcVisualKit.cs").read_text(encoding="utf-8")
    profiles = {template: (profile, build) for template, profile, build in PROFILE_RE.findall(profile_text)}
    records = []
    for path in sorted((ROOT / "scenes/regions").rglob("*.tscn")):
        records.extend(scene_records(path))
    for record in records:
        profile = profiles.get(record["template"])
        record["profile"] = profile[0] if profile else ""
        record["build"] = profile[1] if profile else ""
    with (output / "placed-humans.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=records[0].keys())
        writer.writeheader()
        writer.writerows(records)
    base_counts = Counter(record["base_model"] for record in records)
    profile_counts = Counter(record["profile"] for record in records)
    missing = [record for record in records if not record["profile"]]
    signatures = defaultdict(list)
    for record in records:
        signatures[(record["base_model"], record["profile"])].append(record["display"])
    duplicate_identities = {key: names for key, names in signatures.items() if len(names) > 1}
    lines = [
        "# NPC population audit", "",
        f"- Placed human NPCs: **{len(records)}**",
        f"- Production base meshes used: **{len(base_counts)}**",
        f"- Controlled modular profiles used: **{len(profile_counts)}**",
        f"- Missing profiles: **{len(missing)}**",
        f"- Identical base/profile identity collisions: **{len(duplicate_identities)}**",
        "", "## Base-mesh reuse before modular profiles", "",
        "| Base model | Placed identities |", "| --- | ---: |",
    ]
    lines.extend(f"| `{model}` | {count} |" for model, count in base_counts.most_common())
    lines += ["", "## Coverage", ""]
    if missing:
        lines.extend(f"- MISSING `{item['template']}` ({item['display']}) in `{item['scene']}`" for item in missing)
    else:
        lines.append("Every placed human TemplateId resolves to a controlled visual profile.")
    lines += ["", "## Remaining identical combinations", ""]
    if duplicate_identities:
        for (model, profile), names in sorted(duplicate_identities.items()):
            lines.append(f"- `{model}` + `{profile}`: {', '.join(names)}")
    else:
        lines.append("No two placed identities share both the same base mesh and the same modular profile.")
    (output / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"NPC population audit: {len(records)} placed, {len(profiles)} profiles, "
          f"{len(missing)} missing, {len(duplicate_identities)} identical combinations -> {output}")
    raise SystemExit(1 if missing or duplicate_identities else 0)


if __name__ == "__main__":
    main()
