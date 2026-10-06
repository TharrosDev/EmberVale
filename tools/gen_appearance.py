#!/usr/bin/env python3
"""Author the character appearance catalogue (P8).

One table drives three things that must agree, so they cannot drift:
  1. data/appearance/*.tres        - one AppearanceOptionResource per option
  2. data/races/*.tres             - each race's AppearanceOptionIds allow-list (what the creator offers it)
  3. data/locale/strings.csv       - the option names, in the '# --- BEGIN progression:appearance ---' block

The slot-default options (IsDefault) reproduce the unmodified model; their tints are the reference colours in
src/Appearance/AppearanceRules.cs (printed by tools/gen_player_mask.py). Every race offers every slot's default.

Idempotent: re-running rewrites the generated .tres files and the locale block.
Usage:  python tools/gen_appearance.py
"""

import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SLOT = {"skin": 0, "hair": 1, "eyes": 2, "ember": 3, "build": 4}

# slot -> [(name, display name, (r, g, b) or build scale, is_default)]
OPTIONS = {
    "skin": [
        ("fair", "Fair", (0.676, 0.506, 0.439), True),
        ("pale", "Pale", (0.80, 0.68, 0.62), False),
        ("tan", "Tan", (0.60, 0.43, 0.30), False),
        ("olive", "Olive", (0.55, 0.45, 0.30), False),
        ("brown", "Brown", (0.42, 0.28, 0.20), False),
        ("deep", "Deep", (0.28, 0.18, 0.14), False),
        ("ashen", "Ashen", (0.45, 0.43, 0.47), False),
        ("silver", "Silver", (0.66, 0.68, 0.78), False),
        ("moss", "Moss", (0.40, 0.50, 0.32), False),
        ("slate", "Slate", (0.45, 0.50, 0.52), False),
        ("copper", "Copper", (0.62, 0.34, 0.22), False),
        ("scarlet", "Scarlet", (0.60, 0.24, 0.18), False),
        ("gold", "Gold", (0.78, 0.62, 0.30), False),
    ],
    "hair": [
        ("brown", "Brown", (0.191, 0.153, 0.125), True),
        ("black", "Black", (0.07, 0.06, 0.06), False),
        ("auburn", "Auburn", (0.42, 0.18, 0.10), False),
        ("blonde", "Blonde", (0.80, 0.66, 0.38), False),
        ("ginger", "Ginger", (0.62, 0.28, 0.10), False),
        ("grey", "Grey", (0.55, 0.55, 0.57), False),
        ("white", "White", (0.88, 0.88, 0.90), False),
        ("ash", "Ash", (0.28, 0.27, 0.30), False),
        ("cinder", "Cinder red", (0.70, 0.16, 0.08), False),
        ("moss", "Moss", (0.22, 0.38, 0.20), False),
        ("midnight", "Midnight", (0.08, 0.10, 0.20), False),
        ("violet", "Violet", (0.50, 0.38, 0.62), False),
    ],
    "eyes": [
        ("brown", "Brown", (0.287, 0.213, 0.179), True),
        ("blue", "Blue", (0.30, 0.50, 0.85), False),
        ("green", "Green", (0.30, 0.65, 0.35), False),
        ("amber", "Amber", (0.80, 0.55, 0.15), False),
        ("grey", "Grey", (0.55, 0.58, 0.62), False),
        ("violet", "Violet", (0.60, 0.35, 0.85), False),
        ("gold", "Gold", (0.95, 0.78, 0.25), False),
        ("red", "Red", (0.80, 0.15, 0.10), False),
        ("ice", "Ice", (0.65, 0.85, 0.95), False),
    ],
    "ember": [
        ("ember", "Ember", (0.82, 0.34, 0.10), True),
        ("gold", "Gold", (0.95, 0.70, 0.15), False),
        ("violet", "Violet", (0.55, 0.30, 0.90), False),
        ("azure", "Azure", (0.25, 0.60, 0.95), False),
        ("verdant", "Verdant", (0.30, 0.80, 0.35), False),
        ("crimson", "Crimson", (0.85, 0.12, 0.15), False),
        ("pale", "Pale", (0.90, 0.90, 0.95), False),
    ],
    "build": [
        ("slim", "Slim", 0.92, False),
        ("average", "Average", 1.0, True),
        ("broad", "Broad", 1.1, False),
    ],
}

# race file name -> slot -> non-default option names the race offers (the slot default is always offered).
EMBER_ALL = ["gold", "violet", "azure", "verdant", "crimson", "pale"]
RACES = {
    "Human": {
        "skin": ["pale", "tan", "olive", "brown", "deep"],
        "hair": ["black", "auburn", "blonde", "ginger", "grey"],
        "eyes": ["blue", "green", "amber", "grey"],
        "ember": EMBER_ALL, "build": ["slim", "broad"],
    },
    "Sylthari": {
        "skin": ["pale", "olive", "tan", "silver", "moss"],
        "hair": ["blonde", "auburn", "white", "moss", "violet"],
        "eyes": ["green", "amber", "gold", "ice"],
        "ember": EMBER_ALL, "build": ["slim"],
    },
    "Grondar": {
        "skin": ["tan", "brown", "moss", "slate", "deep"],
        "hair": ["black", "grey", "ash", "ginger"],
        "eyes": ["amber", "grey", "red"],
        "ember": EMBER_ALL, "build": ["broad"],
    },
    "Valari": {
        "skin": ["pale", "gold", "silver", "tan"],
        "hair": ["blonde", "white", "violet", "midnight"],
        "eyes": ["blue", "violet", "gold", "ice"],
        "ember": EMBER_ALL, "build": ["slim", "broad"],
    },
    "Umbral": {
        "skin": ["pale", "ashen", "deep", "silver"],
        "hair": ["black", "midnight", "ash", "white", "grey"],
        "eyes": ["violet", "red", "grey", "ice"],
        "ember": EMBER_ALL, "build": ["slim", "broad"],
    },
    "Draekyn": {
        "skin": ["tan", "copper", "scarlet", "brown"],
        "hair": ["black", "ginger", "cinder", "ash"],
        "eyes": ["amber", "gold", "red", "green"],
        "ember": EMBER_ALL, "build": ["slim", "broad"],
    },
}

SLOT_LABEL = {"skin": "Skin", "hair": "Hair", "eyes": "Eyes", "ember": "Ember glow", "build": "Build"}
BEGIN = "# --- BEGIN progression:appearance ---"
END = "# --- END progression:appearance ---"


def oid(slot, name):
    return f"appearance.{slot}.{name}"


def write_options():
    out_dir = os.path.join(ROOT, "data", "appearance")
    os.makedirs(out_dir, exist_ok=True)
    keep = set()
    for slot, rows in OPTIONS.items():
        for name, display, value, is_default in rows:
            file = f"{slot.capitalize()}{name.capitalize()}.tres"
            keep.add(file)
            tint = "Color(1, 1, 1, 1)"
            scale = 1.0
            if slot == "build":
                scale = value
            else:
                tint = "Color({:.3f}, {:.3f}, {:.3f}, 1)".format(*value)
            body = (
                '[gd_resource type="Resource" script_class="AppearanceOptionResource" load_steps=2 format=3]\n\n'
                '[ext_resource type="Script" path="res://src/Appearance/AppearanceOptionResource.cs" id="1_opt"]\n\n'
                "[resource]\n"
                'script = ExtResource("1_opt")\n'
                f'Id = "{oid(slot, name)}"\n'
                f'NameKey = "{oid(slot, name)}.name"\n'
                f"Slot = {SLOT[slot]}\n"
                f"Tint = {tint}\n"
                f"BuildScale = {scale}\n"
                f"IsDefault = {'true' if is_default else 'false'}\n"
            )
            with open(os.path.join(out_dir, file), "w", encoding="utf-8", newline="\n") as f:
                f.write(body)
    for file in os.listdir(out_dir):
        if file.endswith(".tres") and file not in keep:
            os.remove(os.path.join(out_dir, file))


def write_races():
    for race, per_slot in RACES.items():
        path = os.path.join(ROOT, "data", "races", f"{race}.tres")
        text = open(path, encoding="utf-8").read()
        ids = []
        for slot in ("skin", "hair", "eyes", "ember", "build"):
            default = next(n for n, _, _, d in OPTIONS[slot] if d)
            for name in [default] + per_slot[slot]:
                ids.append(oid(slot, name))
        line = "AppearanceOptionIds = Array[String]([" + ", ".join(f'"{i}"' for i in ids) + "])"
        text, count = re.subn(r"^AppearanceOptionIds = .*$", line, text, flags=re.M)
        if count == 0:  # a race authored without the field (Human) gets it appended to [resource]
            text = text.rstrip("\n") + "\n" + line + "\n"
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)


def write_locale():
    path = os.path.join(ROOT, "data", "locale", "strings.csv")
    with open(path, encoding="utf-8", newline="") as f:
        text = f.read()
    rows = [BEGIN,
            "# Character appearance (P8): option names (generated by tools/gen_appearance.py) and the creator's appearance section.",
            "create.appearance,Appearance",
            "create.appearance_hint,Looks are cosmetic; they change nothing else."]
    for slot, label in SLOT_LABEL.items():
        rows.append(f"create.appearance.{slot},{label}")
    for slot, items in OPTIONS.items():
        for name, display, _, _ in items:
            rows.append(f"{oid(slot, name)}.name,{display}")
    nl = "\r\n" if "\r\n" in text else "\n"
    block = nl.join(rows) + nl + END + nl
    if BEGIN in text:
        text = re.sub(re.escape(BEGIN) + r".*?" + re.escape(END) + r"\r?\n", lambda m: block, text, flags=re.S)
    else:
        if not text.endswith("\n"):
            text += nl
        text += block
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)


if __name__ == "__main__":
    write_options()
    write_races()
    write_locale()
    print("appearance: options, race allow-lists and locale block written")
