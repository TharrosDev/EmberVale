"""Shared helpers for the W-Spec-2 modules (Pale Concord, Act III, Act IV). Not a spec itself (leading
underscore: the registry does not discover it).

t() lets prose carry speech without escaping: write <<like this>> and get "like this".
"""

from tools.campaign.model import Choice, Dialogue, Node, leave


def t(text: str) -> str:
    return text.replace("<<", '"').replace(">>", '"')


def back(goto="root", text="Something else.", tag="back", **kw) -> Choice:
    from tools.campaign.model import go
    return go(t(text), goto, tag=tag, **kw)


def bye(text="Leave it.", tag="bye", **kw) -> Choice:
    return leave(t(text), tag=tag, **kw)


def read_clue(dialogue_id, speaker, first, then=None, then_button="Read on.", close="Let it be.",
              first_close=None, secret=None, on_enter=None) -> Dialogue:
    """A placed clue / lore stone: one or two nodes, always a way out. `then` is the second node's text."""
    from tools.campaign.model import NO_EFF, go
    nodes = []
    root_choices = []
    if then:
        root_choices.append(go(t(then_button), "more", tag="more"))
    root_choices.append(leave(t(first_close or close), tag="close"))
    nodes.append(Node("root", t(first), root_choices, on_enter=on_enter or NO_EFF))
    if then:
        nodes.append(Node("more", t(then), [leave(t(close), tag="close")]))
    return Dialogue(id=dialogue_id, speaker=t(speaker), start="root", nodes=nodes, secret=secret)
