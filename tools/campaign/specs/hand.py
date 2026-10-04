"""W-Spec-1: locale rows for the additions made to hand-authored dialogues by tools/campaign/extend_hand.py.

No quests or dialogues here: the files are hand-authored and extended in place (append-only). This module only hands
their strings to the generator, which owns the `spec1` block of data/locale/strings.csv.
"""

from tools.campaign.extend_hand import all_rows
from tools.campaign.specs import _s1_hand, _s1_hand2


def build():
    return [], [], all_rows(_s1_hand.EXTENSIONS + _s1_hand2.EXTENSIONS), "spec1"
