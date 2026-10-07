#!/usr/bin/env python3
"""Audit placed human identities and base-mesh reuse."""

from __future__ import annotations

import argparse
import csv
import os
import re
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
EXT_RE = re.compile(r'\[ext_resource type="PackedScene" path="res://assets/models/characters/(npc_[^"]+)" id="([^"]+)"\]')
NODE_RE = re.compile(r'^\[node name="([^"]+)" type="Node3D" parent="\."\]$', re.M)
MODEL_RE = re.compile(r'^\[node name="Model" parent="([^"]+)" instance=ExtResource\("([^"]+)"\)\]$', re.M)


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
    # The default used to be the archived session-03 handoff folder, so a plain run overwrote the
    # evidence it was once written as. It now lands with every other run's output.
    parser.add_argument("--output", default=None,
                        help="default: $EMBERVALE_ARTIFACTS/npc-population, else artifacts/audit/npc-population")
    args = parser.parse_args()
    artifacts = os.environ.get("EMBERVALE_ARTIFACTS")
    output = ROOT / (args.output or (Path(artifacts) / "npc-population" if artifacts
                                     else "artifacts/audit/npc-population"))
    output.mkdir(parents=True, exist_ok=True)
    records = []
    for path in sorted((ROOT / "scenes/regions").rglob("*.tscn")):
        records.extend(scene_records(path))
    with (output / "placed-humans.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=records[0].keys())
        writer.writeheader()
        writer.writerows(records)
    base_counts = Counter(record["base_model"] for record in records)
    sharing = defaultdict(list)
    for record in records:
        sharing[record["base_model"]].append(record["display"] or record["template"])
    lines = [
        "# NPC population audit", "",
        f"- Placed human NPCs: **{len(records)}**",
        f"- Production base meshes used: **{len(base_counts)}**",
        "", "## Base-mesh reuse", "",
        "| Base model | Placed identities |", "| --- | ---: |",
    ]
    lines.extend(f"| `{model}` | {count} |" for model, count in base_counts.most_common())
    lines += ["", "## Who shares a body", ""]
    for model, _ in base_counts.most_common():
        lines.append(f"- `{model}`: {', '.join(sharing[model])}")
    (output / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"NPC population audit: {len(records)} placed on {len(base_counts)} base meshes -> {output}")


if __name__ == "__main__":
    main()
