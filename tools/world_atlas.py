#!/usr/bin/env python3
"""The five-realm atlas: where every realm sits in world space, how they connect, and who owns each
reserved location (Phase 44A's contract, written by the 2026-09 world rebuild).

    python tools/world_atlas.py --check    # gate: bands disjoint, built specs inside their bands,
                                           #       reserved hooks owned, the doc's table current,
                                           #       and no data or UI record leaks the Pale Concord
    python tools/world_atlas.py --table    # print the markdown table docs/WORLD_ATLAS.md embeds

Regions are separate coordinate spaces loaded one at a time and joined by portals; the atlas still
places them geographically — Frostfang north across the Crown Pass, the Ashen Wilds east beyond the
Breach, Sunspire south beyond the Southmarch Gate, the Pale Concord hidden in the west — so that a
direction said in dialogue, a road's heading and a portal's side of the realm all agree, and so that
no two realms can ever share ground.
"""

from __future__ import annotations

import csv
import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))


@dataclass(frozen=True)
class Realm:
    key: str
    name: str
    region_id: str
    band: tuple[float, float, float, float]   # min x, min z, max x, max z (world metres)
    status: str                                # built | reserved | hidden
    owner: str                                 # the phase that builds or rebuilds it
    identity: str


REALMS = [
    Realm("ember_crown", "The Ember Crown", "region.ember_crown", (-520.0, -720.0, 520.0, 440.0),
          "built", "2026-09 world rebuild",
          "Human heartland: the Crown Range, the Emberwash valley, the Tarn, farm belt and ash flats; "
          "the capital under the Iron Citadel."),
    Realm("frostfang_reach", "Frostfang Reach", "region.frostfang_reach", (-460.0, -2320.0, 500.0, -1300.0),
          "built", "2026-09 world rebuild",
          "Alpine clan country: the Stormbound Vale, one hold, three dragon territories, Stormcrown."),
    Realm("ashen_wilds", "The Ashen Wilds", "region.ashen_wilds", (900.0, -1100.0, 2100.0, 300.0),
          "built", "2026-09 finish run",
          "Cataclysm scar east beyond the Ashen Breach: plateaus, ravines, corrupted forest, the Beast Lord."),
    Realm("sunspire", "The Sunspire Dominion", "region.sunspire", (-800.0, 900.0, 800.0, 2300.0),
          "built", "finish run (2026-09-27)",
          "South beyond the Southmarch Gate: desert basins, jungle belt, the great libraries, the Crimson Prophet."),
    Realm("pale_concord", "The Pale Concord", "region.pale_concord", (-2600.0, -400.0, -1500.0, 900.0),
          "hidden", "finish run (built)",
          "Found by story, never advertised: still fields, canals and a preserved city at dusk under the Hollow Queen. "
          "No text the player can read names it before the reveal flag."),
]

# The hidden realm's secrecy contract (see check()).
PALE_REVEAL_FLAG = "flag.pale_concord_revealed"
PALE_KEY_PREFIXES = ("pale.", "location.pale.")

# Where one realm's road meets the next. Built ends are world points in their own region.
CROSSINGS = [
    ("ember_crown", "frostfang_reach", "the Crown Pass portal (-150, -652)", "the Stormbound Vale gap (-60, -1316)", "built"),
    ("ember_crown", "ashen_wilds", "the Ashen Breach (500, -282)", "the Breach Scar mouth (928, -298)", "built"),
    ("ember_crown", "sunspire", "the Southmarch Gate (72, 402)", "the caravan gap (60, 916)", "built"),
]

# Location hooks later phases depend on, and who owns them. A reserved id must NOT exist as a map
# location until its realm is built; a built hook must exist.
HOOKS = [
    ("location.ember_crown.southmarch_gate", "ember_crown", "built", "the Sunspire road's end (44K)"),
    ("location.ember_crown.ashen_breach", "ember_crown", "built", "the Ashen Wilds road's end (44G)"),
    ("location.frostfang.stormcrown", "frostfang_reach", "built", "Storm Tyrant territory (44E / 47E)"),
    ("location.ashen.station", "ashen_wilds", "built", "Ash Hunters' field station (42E, owned by 44H)"),
    ("location.sunspire.library", "sunspire", "built", "Veiled Archive great library (42G, owned by 44L)"),
]

# Travel-distance bands the built realms are laid out against (2026-09 world rebuild), in metres of
# straight line between content origins. Walk 5.5 m/s, trot 9.35 m/s.
DISTANCE_BANDS = [
    ("district to district (capital)", 60, 180),
    ("capital to satellite settlement", 330, 560),
    ("settlement to settlement", 260, 500),
    ("capital to frontier / boss territory", 540, 900),
]


def table() -> str:
    lines = ["| Realm | Band (x; z) | Status | Owner | Identity |", "| --- | --- | --- | --- | --- |"]
    for r in REALMS:
        lines.append(f"| {r.name} | {r.band[0]:g}..{r.band[2]:g}; {r.band[1]:g}..{r.band[3]:g} | "
                     f"{r.status} | {r.owner} | {r.identity} |")
    return "\n".join(lines)


def overlaps(a, b) -> bool:
    return not (a[2] <= b[0] or b[2] <= a[0] or a[3] <= b[1] or b[3] <= a[1])


def check() -> list[str]:
    issues = []
    for i, a in enumerate(REALMS):
        for b in REALMS[i + 1:]:
            if overlaps(a.band, b.band):
                issues.append(f"bands overlap: {a.key} and {b.key}")

    import region_spec_ember
    import region_spec_frostfang
    import region_spec_ashen
    import region_spec_sunspire
    import region_spec_pale_concord
    for realm, spec in (("ember_crown", region_spec_ember), ("frostfang_reach", region_spec_frostfang),
                        ("ashen_wilds", region_spec_ashen),
                        ("sunspire", region_spec_sunspire),
                        ("pale_concord", region_spec_pale_concord)):
        band = next(r.band for r in REALMS if r.key == realm)
        lattice = (spec.EXTENT_X[0], spec.ROWS[0][0], spec.EXTENT_X[1], spec.ROWS[-1][1])
        if not (band[0] <= lattice[0] and band[1] <= lattice[1] and lattice[2] <= band[2] and lattice[3] <= band[3]):
            issues.append(f"{realm}: lattice {lattice} leaves its atlas band {band}")

    locations = {m.group(1) for p in (ROOT / "data" / "map_locations").glob("*.tres")
                 for m in [re.search(r'^Id = "([^"]+)"', p.read_text(encoding="utf-8-sig"), re.M)] if m}
    for hook, realm, status, why in HOOKS:
        exists = hook in locations
        if status == "built" and not exists:
            issues.append(f"hook {hook} ({why}) is owned by a built realm but no map location declares it")
        if status == "reserved" and exists:
            issues.append(f"hook {hook} ({why}) is reserved for {realm} but already exists")

    # ⚠️ PALE CONCORD SECRECY (Phase 44K/44O; finish run). Ids are not leaks: region.pale_concord, its
    # flags, scene paths and resource ids may appear anywhere (other realms' portals and quests need
    # them). What must not name the realm before its reveal is PLAYER-VISIBLE TEXT:
    #   * a locale VALUE, unless its key is post-reveal text (a PALE_KEY_PREFIXES key, shown only
    #     inside the realm or after the reveal flag);
    #   * a map location, unless it is gated on the reveal flag. Every location inside the realm's
    #     cells must be gated, since discovery would otherwise list and search it.
    leak = re.compile(r"pale[ _]concord", re.I)
    strings: dict[str, str] = {}
    for row in csv.reader((ROOT / "data" / "locale" / "strings.csv").read_text(
            encoding="utf-8-sig", errors="ignore").splitlines()):
        if len(row) >= 2 and row[0] and not row[0].startswith("#"):
            strings[row[0]] = ",".join(row[1:])
            if leak.search(strings[row[0]]) and not row[0].startswith(PALE_KEY_PREFIXES):
                issues.append(f"Pale Concord leak: locale value of '{row[0]}' (key it under pale.*)")
    for path in (ROOT / "data" / "map_locations").glob("*.tres"):
        text = path.read_text(encoding="utf-8-sig", errors="ignore")
        field = lambda name: (re.search(rf'^{name} = "([^"]*)"', text, re.M) or [None, ""])[1]
        named = any(leak.search(strings.get(field(k), "")) for k in ("NameKey", "DescriptionKey"))
        if (field("CellId").startswith("pale_concord.") or named) and field("RequiredFlagId") != PALE_REVEAL_FLAG:
            issues.append(f"Pale Concord leak: {path.relative_to(ROOT)} is not gated on {PALE_REVEAL_FLAG}")

    doc = ROOT / "docs" / "WORLD_ATLAS.md"
    if not doc.exists() or table() not in doc.read_text(encoding="utf-8"):
        issues.append("docs/WORLD_ATLAS.md does not embed the current atlas table (run --table)")
    return issues


def main(argv: list[str]) -> int:
    if "--table" in argv:
        print(table())
        return 0
    issues = check()
    for issue in issues:
        print(f"ATLAS: {issue}")
    print("atlas: " + ("FAIL" if issues else "PASS"))
    return 1 if issues else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
