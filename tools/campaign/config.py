"""Campaign-wide constants the graph validator and the xUnit CampaignGraphTests share (via the JSON).

Spec modules may add to CODE_FLAGS / TERMINAL_FLAGS by declaring module-level lists of the same
name; the registry merges them into the graph (`codeFlags`, `terminalFlags`).
"""

# Flags a quest or dialogue may read that NO quest/dialogue .tres writes: code, scene or story-rule
# writers. Boss defeat flags are not listed here; they are derived from data/bosses/*.tres
# DefeatFlagId. Each entry names its writer so a stale one is easy to spot.
CODE_FLAGS = {
    # src/Narrative/HiddenRealmReveal.cs RevealedFlag: set when three known-realm Flamebearers have fallen.
    "flag.pale_concord_revealed": "HiddenRealmReveal",
    # src/UI/EndingSequence.cs CompleteFlag: set by the ending sequence when the credits play.
    "flag.game_complete": "EndingSequence",
}

# Completion flags that may have no consumer. Anything else a main quest sets must be read by a
# quest auto-start, an objective gate, a dialogue condition, a Milestone, or another data/scene/code
# file (a chain gap otherwise).
TERMINAL_FLAGS = {
    "flag.game_complete": "the last quest's completion; nothing follows the credits",
}

# Quests that exist at New Game without an auto-start flag or a dialogue that starts them.
ENTRY_QUESTS = {}
