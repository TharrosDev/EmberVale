"""Batch Meshy text-to-3D: preview -> refine (texture) -> optional rig, resumable.

    python tools/meshy_batch.py PLAN.json OUT_DIR [--cap 1300] [--only id,id] [--stage preview|refine|rig]

PLAN.json is a list of {id, prompt, texturePrompt?, targetPolycount, poseMode?, rig?, heightMetres}.
OUT_DIR/state.json records every task id the moment it is created, so a re-run never pays twice:
it resumes polling and only starts stages that have no task id yet. Needs MESHY_API_KEY.
Costs (meshy-5): preview 5, refine 10, rig 5. Stops creating tasks once --cap would be passed.
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

API = "https://api.meshy.ai/openapi"
COST = {"preview": 5, "refine": 10, "rig": 5}
STAGES = ["preview", "refine", "rig"]


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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("plan")
    ap.add_argument("out")
    ap.add_argument("--cap", type=int, default=1300)
    ap.add_argument("--only", default="")
    ap.add_argument("--skip", default="")
    ap.add_argument("--max-priority", type=int, default=9)
    ap.add_argument("--stage", choices=STAGES, default="rig", help="last stage to run")
    a = ap.parse_args()

    plan = json.load(open(a.plan, encoding="utf-8"))
    plan = plan["generations"] if isinstance(plan, dict) else plan
    if a.only:
        plan = [p for p in plan if p["id"] in a.only.split(",")]
    plan = [p for p in plan if p["id"] not in a.skip.split(",") and p.get("priority", 1) <= a.max_priority]
    os.makedirs(a.out, exist_ok=True)
    state_path = os.path.join(a.out, "state.json")
    state = json.load(open(state_path)) if os.path.exists(state_path) else {}
    last = STAGES.index(a.stage)

    def save():
        tmp = state_path + ".tmp"
        json.dump(state, open(tmp, "w"), indent=1)
        os.replace(tmp, state_path)

    def spent():
        return sum(COST[s] for st in state.values() for s in STAGES if s in st)  # rejected attempts stay in state under "<id>#rejectedN" so they still count

    while True:
        busy = False
        for item in plan:
            st = state.setdefault(item["id"], {})
            for i, stage in enumerate(STAGES[:last + 1]):
                if stage == "rig" and not item.get("rig"):
                    break
                cur = st.get(stage)
                if cur is None:
                    if i and st[STAGES[i - 1]]["status"] != "SUCCEEDED":
                        break
                    if spent() + COST[stage] > a.cap:
                        print(f"CAP: not starting {stage} for {item['id']} (spent {spent()})")
                        break
                    try:
                        task = create(stage, item, st)
                    except RuntimeError as e:
                        print(f"CREATE FAILED {item['id']} {stage}: {e}")
                        st["error"] = str(e)
                        save()
                        break
                    cur = st[stage] = {"task": task, "status": "PENDING"}
                    save()
                    print(f"started {item['id']} {stage} {task} (spent {spent()})")
                if cur["status"] in ("PENDING", "IN_PROGRESS"):
                    res = poll(stage, cur["task"])
                    cur["status"] = res.get("status", cur["status"])
                    if cur["status"] == "SUCCEEDED":
                        url = glb_url(stage, res)
                        dest = os.path.join(a.out, f"{item['id']}.{stage}.glb")
                        urllib.request.urlretrieve(url, dest)
                        cur["file"] = dest
                        if stage != "rig" and res.get("thumbnail_url"):
                            urllib.request.urlretrieve(res["thumbnail_url"],
                                                       os.path.join(a.out, f"{item['id']}.{stage}.png"))
                        print(f"done {item['id']} {stage}")
                    elif cur["status"] in ("FAILED", "CANCELED", "EXPIRED"):
                        cur["error"] = json.dumps(res.get("task_error"))
                        print(f"FAILED {item['id']} {stage}: {cur['error']}")
                    else:
                        busy = True
                    save()
                if cur["status"] != "SUCCEEDED":
                    break
        if not busy:
            break
        time.sleep(15)

    bad = [k for k, st in state.items() if any(st.get(s, {}).get("status") not in (None, "SUCCEEDED") for s in STAGES)]
    print(f"spent {spent()} credits; {len(state)} items; failed: {bad or 'none'}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
