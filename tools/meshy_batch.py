"""Batch Meshy text-to-3D: preview -> refine (texture) -> optional rig, resumable.

    python tools/meshy_batch.py PLAN.json OUT_DIR --dry-run        what it would start and cost; no network
    python tools/meshy_batch.py PLAN.json OUT_DIR --status         where every item stands; no network
    python tools/meshy_batch.py PLAN.json OUT_DIR [--cap 1300] [--only id,id] [--skip id,id]
                                [--stage preview|refine|rig] [--max-minutes 9] [--sheet] [--no-ledger]
    python tools/meshy_batch.py PLAN.json OUT_DIR --ledger         only append finished items to the ledger

PLAN.json is a list of {id, prompt, texturePrompt?, targetPolycount, poseMode?, rig?, heightMetres}.
OUT_DIR/state.json records every task id the moment it is created, so a re-run never pays twice:
it resumes polling and only starts stages that have no task id yet. Needs MESHY_API_KEY to run;
--dry-run, --status and --ledger need neither the key nor the network and spend nothing.
Costs (meshy-5): preview 5, refine 10, rig 5. Stops creating tasks once --cap would be passed.

A run stops after --max-minutes (default 9, inside a ten-minute foreground command) and exits 3;
the same command resumes it. Every item that reaches its last stage is appended to the ledger
reports/3d/archive/meshy-migration/manifest.csv with status `generated` (existing rows are never
rewritten; a task id already in the file is skipped), because a task that is in neither
assets/models/ nor the ledger costs credits again once it expires. The last line is `MESHY {json}`.
Exit 0 finished, 1 a task failed, 3 work still pending.
"""
import argparse
import csv
import datetime
import io
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

API = "https://api.meshy.ai/openapi"
COST = {"preview": 5, "refine": 10, "rig": 5}
STAGES = ["preview", "refine", "rig"]
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LEDGER = os.path.join(ROOT, "reports", "3d", "archive", "meshy-migration", "manifest.csv")
LEDGER_COLUMNS = ["asset", "category", "entity", "source", "meshy_task_id", "rig_task_id", "prompt",
                  "animations", "refs_updated", "status", "notes"]
FAILED = ("FAILED", "CANCELED", "EXPIRED")


def call(method, path, body=None):
    req = urllib.request.Request(
        API + path, method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={"Authorization": "Bearer " + os.environ["MESHY_API_KEY"],
                 "Content-Type": "application/json"})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            text = e.read().decode(errors="replace")
            # a POST is never retried on a server error: the task may exist and be billed
            if e.code in (429, 502, 503, 504) and (method == "GET" or e.code == 429):
                time.sleep(10 * (attempt + 1))
                continue
            raise RuntimeError(f"{method} {path} -> {e.code} {text}")
        except (urllib.error.URLError, TimeoutError) as e:
            if method != "GET":
                raise RuntimeError(f"{method} {path} failed in transit ({e}); check the account before retrying")
            time.sleep(10 * (attempt + 1))
    raise RuntimeError(f"{method} {path} kept failing")


def create(stage, item, st):
    if stage == "preview":
        body = {"mode": "preview", "prompt": item["prompt"], "ai_model": "meshy-5",
                "topology": "triangle", "target_polycount": int(item["targetPolycount"]),
                "should_remesh": True, "target_formats": ["glb"]}
        if item.get("poseMode"):
            body["pose_mode"] = item["poseMode"]
        return call("POST", "/v2/text-to-3d", body)["result"]
    if stage == "refine":
        body = {"mode": "refine", "preview_task_id": st["preview"]["task"], "ai_model": "meshy-5",
                "enable_pbr": bool(item.get("pbr")), "target_formats": ["glb"]}
        if item.get("texturePrompt"):
            body["texture_prompt"] = item["texturePrompt"]
        return call("POST", "/v2/text-to-3d", body)["result"]
    body = {"input_task_id": st["refine"]["task"], "height_meters": float(item.get("heightMetres") or 1.8)}
    return call("POST", "/v1/rigging", body)["result"]


def poll(stage, task):
    return call("GET", ("/v1/rigging/" if stage == "rig" else "/v2/text-to-3d/") + task)


def glb_url(stage, res):
    if stage == "rig":
        return (res.get("result") or {}).get("rigged_character_glb_url")
    return (res.get("model_urls") or {}).get("glb")


def download(url, dest, timeout=120):
    """Bounded download to a temporary name, so a stalled or cut transfer never leaves a short file."""
    tmp = dest + ".part"
    with urllib.request.urlopen(url, timeout=timeout) as response, open(tmp, "wb") as out:
        while chunk := response.read(1 << 16):
            out.write(chunk)
    os.replace(tmp, dest)


# ------------------------------------------------------------------------------------- pure state

def load_plan(path, only="", skip="", max_priority=9):
    with open(path, encoding="utf-8") as handle:
        plan = json.load(handle)
    plan = plan["generations"] if isinstance(plan, dict) else plan
    if only:
        plan = [p for p in plan if p["id"] in only.split(",")]
    return [p for p in plan if p["id"] not in skip.split(",") and p.get("priority", 1) <= max_priority]


def load_state(path):
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def save_state(path, state):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=1)
    os.replace(tmp, path)


def spent(state):
    # rejected attempts stay in state under "<id>#rejectedN" so they still count
    return sum(COST[s] for st in state.values() for s in STAGES if s in st)


def wanted(item, last):
    """The stages this item runs, in order: rig only when the plan asks for one."""
    return [s for s in STAGES[:last + 1] if s != "rig" or item.get("rig")]


def status_rows(plan, state, last):
    rows = []
    for item in plan:
        st = state.get(item["id"], {})
        stages = {s: st.get(s, {}).get("status", "-") for s in wanted(item, last)}
        if any(v in FAILED for v in stages.values()) or (st.get("error") and "-" in stages.values()):
            overall = "failed"
        elif all(v == "SUCCEEDED" for v in stages.values()):
            overall = "done"
        else:
            overall = "pending"
        rows.append({"id": item["id"], "state": overall, "stages": stages,
                     "credits": sum(COST[s] for s in STAGES if s in st),
                     **({"error": st["error"]} if st.get("error") else {})})
    return rows


def status_exit(rows):
    states = {row["state"] for row in rows}
    return 1 if "failed" in states else 3 if "pending" in states else 0


def cost_preview(plan, state, last, cap):
    """What a run would still create and pay for. Stages are paid in plan order until the cap."""
    already = spent(state)
    needed, within, blocked = 0, [], []
    running = already
    for item in plan:
        st = state.get(item["id"], {})
        for stage in wanted(item, last):
            if stage in st:
                if st[stage].get("status") in FAILED:
                    break  # a failed stage is never re-created; later stages cannot start
                continue
            needed += COST[stage]
            if running + COST[stage] <= cap:
                running += COST[stage]
                within.append(f"{item['id']}:{stage}")
            else:
                blocked.append(f"{item['id']}:{stage}")
    return {"spent": already, "needed": needed, "cap": cap, "headroom": cap - already,
            "would_spend": running - already, "would_start": within, "blocked_by_cap": blocked}


# ------------------------------------------------------------------------------------- ledger

def ledger_rows(plan, state, last, existing_text, today):
    """Rows for items whose last stage succeeded and whose task id is not in the ledger yet."""
    rows = []
    for item in plan:
        st = state.get(item["id"], {})
        stages = wanted(item, last)
        if not stages or any(st.get(s, {}).get("status") != "SUCCEEDED" for s in stages):
            continue
        model_stage = "refine" if "refine" in stages else "preview"
        task = st[model_stage]["task"]
        if task in existing_text:
            continue
        credits = sum(COST[s] for s in STAGES if s in st)
        notes = f"tools/meshy_batch.py {today}; {credits} credits; not adopted yet"
        # Column wording follows the rows already in the ledger.
        rows.append({
            "asset": item.get("dest") or item.get("asset") or f"(not adopted) {item['id']}",
            "category": item.get("category") or item.get("kind") or "world",
            "entity": item.get("entity") or item["id"],
            "source": "custom-meshy",
            "meshy_task_id": "; ".join(f"{s} {st[s]['task']}" for s in stages if s != "rig"),
            "rig_task_id": st["rig"]["task"] if "rig" in stages else "none (static)",
            "prompt": "text-to-3d meshy-5: " + item.get("prompt", ""),
            "animations": "none",
            "refs_updated": "none",
            "status": "generated",
            "notes": notes,
        })
    return rows


def append_ledger(path, rows):
    """Append only. Existing bytes, including the file's own line endings, are left alone."""
    if not rows:
        return 0
    with open(path, "rb") as handle:
        existing = handle.read()
    newline = "\r\n" if b"\r\n" in existing else "\n"
    buffer = io.StringIO()
    writer = csv.DictWriter(buffer, fieldnames=LEDGER_COLUMNS, lineterminator=newline)
    for row in rows:
        writer.writerow(row)
    lead = "" if existing.endswith(b"\n") or not existing else newline
    with open(path, "ab") as handle:
        handle.write((lead + buffer.getvalue()).encode("utf-8"))
    return len(rows)


def update_ledger(plan, state, last, path=LEDGER, today=None):
    if not os.path.exists(path):
        return 0
    with open(path, encoding="utf-8", errors="replace", newline="") as handle:
        existing = handle.read()
    today = today or datetime.date.today().isoformat()
    return append_ledger(path, ledger_rows(plan, state, last, existing, today))


# ------------------------------------------------------------------------------------- run

def run(plan, state, save, last, cap, out, budget_seconds,
        create=create, poll=poll, fetch=download, sleep=time.sleep, clock=time.monotonic, say=print):
    """Advance every item as far as it will go. Returns "done" or "budget" (still polling)."""
    started = clock()
    while True:
        busy = False
        for item in plan:
            st = state.setdefault(item["id"], {})
            stages = wanted(item, last)
            for i, stage in enumerate(stages):
                cur = st.get(stage)
                if cur is None:
                    if i and st[stages[i - 1]]["status"] != "SUCCEEDED":
                        break
                    if spent(state) + COST[stage] > cap:
                        say(f"CAP: not starting {stage} for {item['id']} (spent {spent(state)})")
                        break
                    try:
                        task = create(stage, item, st)
                    except RuntimeError as e:
                        say(f"CREATE FAILED {item['id']} {stage}: {e}")
                        st["error"] = str(e)
                        save()
                        break
                    cur = st[stage] = {"task": task, "status": "PENDING"}
                    st.pop("error", None)
                    save()
                    say(f"started {item['id']} {stage} {task} (spent {spent(state)})")
                if cur["status"] in ("PENDING", "IN_PROGRESS"):
                    res = poll(stage, cur["task"])
                    cur["status"] = res.get("status", cur["status"])
                    if cur["status"] == "SUCCEEDED":
                        dest = os.path.join(out, f"{item['id']}.{stage}.glb")
                        fetch(glb_url(stage, res), dest)
                        cur["file"] = dest
                        if stage != "rig" and res.get("thumbnail_url"):
                            fetch(res["thumbnail_url"], os.path.join(out, f"{item['id']}.{stage}.png"))
                        say(f"done {item['id']} {stage}")
                    elif cur["status"] in FAILED:
                        cur["error"] = json.dumps(res.get("task_error"))
                        say(f"FAILED {item['id']} {stage}: {cur['error']}")
                    else:
                        busy = True
                    save()
                if cur["status"] != "SUCCEEDED":
                    break
        if not busy:
            return "done"
        if clock() - started >= budget_seconds:
            return "budget"
        sleep(15)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("plan")
    ap.add_argument("out")
    ap.add_argument("--cap", type=int, default=1300)
    ap.add_argument("--only", default="")
    ap.add_argument("--skip", default="")
    ap.add_argument("--max-priority", type=int, default=9)
    ap.add_argument("--stage", choices=STAGES, default="rig", help="last stage to run")
    ap.add_argument("--max-minutes", type=float, default=9.0, help="stop polling after this long and exit 3")
    ap.add_argument("--dry-run", action="store_true", help="print what would start and its cost; no network")
    ap.add_argument("--status", action="store_true", help="print every item's stage states; no network")
    ap.add_argument("--ledger", action="store_true", help="only append finished items to the ledger; no network")
    ap.add_argument("--no-ledger", action="store_true", help="do not append finished items to the ledger")
    ap.add_argument("--sheet", action="store_true", help="write OUT/contact.png from the thumbnails")
    ap.add_argument("--json", action="store_true", help="with --status/--dry-run: one JSON object")
    a = ap.parse_args(argv)

    plan = load_plan(a.plan, a.only, a.skip, a.max_priority)
    state_path = os.path.join(a.out, "state.json")
    state = load_state(state_path)
    last = STAGES.index(a.stage)

    if a.status:
        rows = status_rows(plan, state, last)
        code = status_exit(rows)
        result = {"mode": "status", "items": len(rows), "spent": spent(state), "exit_code": code,
                  **{name: sum(1 for r in rows if r["state"] == name) for name in ("done", "pending", "failed")}}
        if a.json:
            print(json.dumps({**result, "rows": rows}))
            return code
        for row in rows:
            stages = " ".join(f"{s} {v}" for s, v in row["stages"].items())
            print(f"{row['id']}  {row['state']}  {stages}  {row['credits']}cr" + (f"  {row['error']}" if row.get("error") else ""))
        print("MESHY " + json.dumps(result))
        return code

    if a.dry_run:
        preview = cost_preview(plan, state, last, a.cap)
        if a.json:
            print(json.dumps({"mode": "dry-run", **preview}))
            return 0
        for name in preview["blocked_by_cap"][:10]:
            print(f"CAP would block {name}")
        compact = {**preview, "would_start": len(preview["would_start"]), "blocked_by_cap": len(preview["blocked_by_cap"])}
        print("MESHY " + json.dumps({"mode": "dry-run", **compact}))
        return 0

    if a.ledger:
        added = update_ledger(plan, state, last)
        print("MESHY " + json.dumps({"mode": "ledger", "ledger_rows_added": added}))
        return 0

    if not os.environ.get("MESHY_API_KEY"):
        print("MESHY_API_KEY is not set; --dry-run, --status and --ledger work without it", file=sys.stderr)
        return 2
    os.makedirs(a.out, exist_ok=True)
    outcome = run(plan, state, lambda: save_state(state_path, state), last, a.cap, a.out, a.max_minutes * 60)

    added = 0 if a.no_ledger else update_ledger(plan, state, last)
    if a.sheet:
        sheet = os.path.join(a.out, "contact.png")
        subprocess.run([sys.executable, os.path.join(ROOT, "tools", "make_contact_sheet.py"), a.out, sheet],
                       check=False, timeout=300)
    rows = status_rows(plan, state, last)
    bad = [row["id"] for row in rows if row["state"] == "failed"]
    code = 1 if bad else 3 if outcome == "budget" else 0
    if outcome == "budget":
        print(f"still running after {a.max_minutes:g} min: re-run the same command to resume")
    print("MESHY " + json.dumps({"mode": "run", "outcome": outcome, "spent": spent(state), "items": len(rows),
                                 "failed": bad, "ledger_rows_added": added, "exit_code": code}))
    return code


if __name__ == "__main__":
    sys.exit(main())
