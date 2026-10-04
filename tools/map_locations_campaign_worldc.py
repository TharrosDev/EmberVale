"""Campaign overhaul, workstream World-C: map locations for the Pale Concord and Celestial Realm cells.

Loaded by tools/gen_map_locations.py, which calls register(add) with its own add(). Pale places carry the
reveal flag and a pale.* tail, so their locale rows fall under the atlas gate's post-reveal prefix. Each
anchor is the placed thing the location IS: the marker takes its position from that node.
"""

PALE_REVEALED = "flag.pale_concord_revealed"


def register(add):
    add("pale_concord/processional", "pale.court_approach", "Landmark", "Nav/CountStone", "The Court Approach",
        required_flag=PALE_REVEALED,
        desc="The last stretch of the processional way, where it ends at the count-stone below the palace terrace.")
    add("celestial/fallen_choir", "celestial.fallen_choir", "Landmark", "ChoirEcho", "The Fallen Choir",
        desc="A ruined nave on the western terraces, where the choir that sang the gods to their war still answers.")
    add("celestial/godfall", "celestial.godfall", "Landmark", "Nav/AnchorB", "Godfall",
        desc="The broken ground where a god's hall came down, with three split anchors up the flank.")
    add("celestial/sundered_stair", "celestial.sundered_stair", "Landmark", "StairMark", "The Sundered Stair",
        desc="A stair to nowhere on the east flank of the gate terrace, worked by two black iron winches.")
