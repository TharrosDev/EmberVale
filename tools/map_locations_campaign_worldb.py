"""Campaign map locations authored by the World-B workstream (Ashen Wilds and Sunspire cells).

Loaded by tools/gen_map_locations.py (register(add) is called with its own add()). Anchors are nodes
that already exist in the cell scene; the generator parents the MapLocationComponent marker to them.
"""


def register(add):
    # WR-spec1-40: the herd trail, where the Hunters' stakes lead (quest.main.herd_and_hearth). The anchor
    # is the HerdTrail node at the heart of the herd's shallow of ash in the lair approach cell.
    add("ashen_wilds/lair_approach", "ashen.herd_trail", "Landmark", "HerdTrail", "The Herd Trail",
        desc="A road trampled hand-deep by wolf and elk and boar, running to the foot of the plateau ramp.")

    # WR-spec2 (e): the vault under the great library (quest.main.deep_stacks). The anchor is the codex
    # lectern at the heart of the walled court, so the pin is the place a Defend objective holds.
    add("sunspire/library", "sunspire.deep_stacks", "Dungeon", "SunderingCodex", "The Deep Stacks",
        tier="Secondary", desc="The vault under the great library where the gods' own record is kept.")
