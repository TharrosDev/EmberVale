#!/usr/bin/env python3
"""Emit the standard guild-officer conversation graph as a `.tres` (Phase 42B).

Why this exists
---------------
Fourteen hub officers across five guilds all hold the same conversation: a neutral hail, then one
of two mutually exclusive greetings depending on whether the player is one of them. Only the ids
and the locale keys differ, which is precisely the case `tools/gen_merchant_dialogue.py` was
committed for after being written inline three times.

⚠️ **It emits the SCAFFOLD, never the prose.** The locale rows in `data/locale/strings.csv` are
hand-written, for that generator's reason: a generated line of dialogue is a line nobody trusts in
six months.

Usage
-----
    python tools/gen_guild_dialogue.py dawnwarden_captain dialogue.dawnwarden_captain \\
        faction.dawnwardens "Captain Aldric Fenn" > data/dialogue/DawnwardenCaptain.tres

    key       the locale key stem, e.g. `dawnwarden_captain` -> dlg.dawnwarden_captain.root, ...
    id        the DialogueResource id
    faction   the guild's faction id — the CONDITION ARGUMENT, and the only place it appears
    speaker   a human name, used only in the header comment

⚠️ There is no rank argument, and that is a decision. The member branch always asks for rank 0
("a member of any rank") because the pair below has to be EXHAUSTIVE: gate it on rank 1 and a player
who has joined but not yet been promoted matches neither branch and the root node becomes a dead end
for a state the game can really be in. Rank-gated lines belong to the arc sub-phases that grant the
ranks (42C/E/G/I), which will author them as a THIRD branch rather than by narrowing this one.

What the shape encodes
----------------------
* **The greeting is membership-aware because the first exchange is.** A `DialogueResource` has one
  `StartNodeId` and nothing conditions it, so the root line is a neutral hail — a glance up — and
  the two greetings hang off two mutually exclusive choices. Exactly one of them is ever offered.
* **Condition 14 (`GuildRankAtLeast`) and 15 (`GuildNotMember`), never 5 (`HasFlag`).** Membership
  is `guild.<slug>.*` story-flag state that `GuildRules` DERIVES from the faction id; writing one of
  those flags into a `.tres` by hand is what NOW.md invariant 18 forbids, and it would also read a
  rank flag raw — skipping the rules that a player who LEFT is not a member and that a rank gap does
  not promote.
* **The pair is exhaustive and disjoint**, so the root node is never a dead end: every player is
  either a member or not.
"""

import argparse
import sys

TEMPLATE = '''[gd_resource type="Resource" script_class="DialogueResource" format=3]

[ext_resource type="Script" path="res://src/Dialogue/DialogueResource.cs" id="1_dlg"]
[ext_resource type="Script" path="res://src/Dialogue/DialogueNode.cs" id="2_node"]
[ext_resource type="Script" path="res://src/Dialogue/DialogueChoice.cs" id="3_choice"]

; {speaker} ({faction}). Scaffold from tools/gen_guild_dialogue.py; prose is hand-written in
; data/locale/strings.csv. Phase 42B.
;
; The standard guild-officer shape:
;   - the ROOT line is a neutral hail. A DialogueResource has one StartNodeId and nothing
;     conditions it, so the membership-aware greeting is the first EXCHANGE rather than the first
;     line: exactly one of the two choices below is ever offered.
;   - Condition = 14 is GuildRankAtLeast ("{faction}:0" - a member of ANY rank), Condition = 15
;     is GuildNotMember. Rank 0 keeps the pair exhaustive; see the generator header.
;     Never Condition = 5 (HasFlag) with a guild.* flag: those flags are DERIVED by GuildRules from
;     the faction id, and hand-writing one into a .tres is NOW.md invariant 18's whole subject.
;   - the two are exhaustive and disjoint, so root is never a dead end.

[sub_resource type="Resource" id="ch_member"]
script = ExtResource("3_choice")
Text = "dlg.{key}.c_member"
Condition = 14
ConditionArg = "{faction}:0"
Goto = "member"

[sub_resource type="Resource" id="ch_stranger"]
script = ExtResource("3_choice")
Text = "dlg.{key}.c_stranger"
Condition = 15
ConditionArg = "{faction}"
Goto = "stranger"

[sub_resource type="Resource" id="ch_bye"]
script = ExtResource("3_choice")
Text = "dlg.{key}.c_bye"

[sub_resource type="Resource" id="ch_back"]
script = ExtResource("3_choice")
Text = "dlg.{key}.c_back"
Goto = "root"

[sub_resource type="Resource" id="ch_end"]
script = ExtResource("3_choice")
Text = "dlg.{key}.c_end"

[sub_resource type="Resource" id="node_root"]
script = ExtResource("2_node")
Id = "root"
Text = "dlg.{key}.root"
Choices = Array[Resource]([SubResource("ch_member"), SubResource("ch_stranger"), SubResource("ch_bye")])

[sub_resource type="Resource" id="node_member"]
script = ExtResource("2_node")
Id = "member"
Text = "dlg.{key}.member"
Choices = Array[Resource]([SubResource("ch_back"), SubResource("ch_end")])

[sub_resource type="Resource" id="node_stranger"]
script = ExtResource("2_node")
Id = "stranger"
Text = "dlg.{key}.stranger"
Choices = Array[Resource]([SubResource("ch_back"), SubResource("ch_end")])

[resource]
script = ExtResource("1_dlg")
Id = "{did}"
SpeakerName = "dlg.{key}.speaker"
StartNodeId = "root"
Nodes = Array[Resource]([SubResource("node_root"), SubResource("node_member"), SubResource("node_stranger")])
'''

KEYS = ("speaker", "root", "member", "stranger", "c_member", "c_stranger", "c_bye", "c_back", "c_end")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("key")
    ap.add_argument("did")
    ap.add_argument("faction")
    ap.add_argument("speaker")
    ap.add_argument("--keys", action="store_true",
                    help="print the locale keys this file needs instead of the .tres")
    args = ap.parse_args()

    if args.keys:
        for suffix in KEYS:
            print(f"dlg.{args.key}.{suffix}")
        return

    sys.stdout.write(TEMPLATE.format(key=args.key, did=args.did, faction=args.faction,
                                     speaker=args.speaker))


if __name__ == "__main__":
    main()
