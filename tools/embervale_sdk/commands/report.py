"""Existing state/economy/worldgen/lifecycle reports."""

REPORTS = {"state", "economy", "worldgen", "lifecycle"}


def run(run, args, passthrough):
    if args.target not in REPORTS:
        raise ValueError("report requires state, economy, worldgen or lifecycle")
    run.godot(args.target, [], ["--" + args.target])
