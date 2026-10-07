#!/usr/bin/env python3
"""Give the nature/rock prop family ONE copy of each texture it shares.

⚠️ A PROP'S FILE SIZE IS TEXTURE, NOT GEOMETRY, AND THE SAME TEXTURE CAN BE IN THE BUILD
SEVERAL TIMES OVER. The clover model this was written for was 2.5 MB, of which 2.47 MB was a
2048x2048 leaf atlas and 379 triangles was the rest; three more props each embedded a
byte-identical copy of that atlas. Godot then imports separate ImageTextures, so the duplication
is paid twice: once on disk and once in VRAM. (That family was retired with the generated world
set; the tables below now hold only what is still shared.)

Worse, each of those props ALSO left a decoded sidecar `.png` beside it that nothing
references (the GLB is self-contained), which Godot imports and exports anyway.

This tool externalises only the images with more than one user, onto one canonical
`T_Nature_*.png` per family, and deletes the dead sidecars. It rewrites glTF JSON and
compacts the binary chunk; it never touches an accessor, a mesh or a vertex.

⚠️ A SHARED TEXTURE MUST BE A URI, NOT A SECOND COPY. `--shared` adoption is the same
decision `docs/ASSET_POLICY.md` §0.4 records for the medieval megakit, where embedding
fourteen wall modules cost 204 MB of the same six maps fourteen times over.

    python tools/share_nature_textures.py [--check]
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROPS = ROOT / "assets" / "models" / "props"

# canonical shared name -> (sidecar that must match the payload, [(glb, embedded image name), ...])
# Only families with TWO OR MORE users are here. Externalising a single-user texture adds a
# file and saves nothing, so prp_boulder, prp_rock_cluster, prp_mushrooms and the two barks
# stay embedded on purpose.
#
# The nature and rock families that used to be listed here (leaves, flowers, grass, path rocks, the
# broadleaf atlas, the barks, the stone atlas) belonged to models the generated world set replaced.
# Their rows are gone so this tool no longer opens a .glb that has been retired. What is left of
# them on disk needs nothing: `T_Nature_Grass.png` is referenced by URI from the two procedural
# ground-cover meshes (tools/gen_ground_cover.py writes them that way), and every other
# `T_Nature_*.png` is removed by the unreferenced-texture sweep at the end of a normal run once
# the last .glb naming it has been deleted.
FAMILIES: dict[str, tuple[str, list[tuple[str, str]]]] = {
    "T_Prop_Colormap.png": ("prp_station_forge_colormap.png", [
        ("prp_station_forge.glb", "colormap"),
    ]),
}

# Members that embed a different-resolution copy of a family's image are repointed at the
# canonical file and their own payload is discarded. Empty since the stone family was retired.
REPOINTED: dict[str, list[tuple[str, str]]] = {}

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
STRUCTURAL = ("accessors", "meshes", "nodes", "skins", "animations")


def read_glb(path: Path) -> tuple[dict, bytes]:
    raw = path.read_bytes()
    if raw[:4] != b"glTF":
        raise ValueError(f"{path} is not a GLB")
    _, version, length = struct.unpack_from("<III", raw, 0)
    if version != 2 or length != len(raw):
        raise ValueError(f"bad GLB header in {path}")
    document, binary, offset = None, b"", 12
    while offset < length:
        size, kind = struct.unpack_from("<II", raw, offset)
        payload = raw[offset + 8:offset + 8 + size]
        if kind == JSON_CHUNK:
            document = json.loads(payload.rstrip(b" \0"))
        elif kind == BIN_CHUNK:
            binary = payload
        offset += 8 + size
    if document is None:
        raise ValueError(f"no JSON chunk in {path}")
    return document, binary


def write_glb(path: Path, document: dict, binary: bytes) -> None:
    encoded = json.dumps(document, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    encoded += b" " * ((4 - len(encoded) % 4) % 4)
    binary += b"\x00" * ((4 - len(binary) % 4) % 4)
    total = 12 + 8 + len(encoded) + (8 + len(binary) if binary else 0)
    out = bytearray(struct.pack("<III", 0x46546C67, 2, total))
    out += struct.pack("<II", len(encoded), JSON_CHUNK) + encoded
    if binary:
        out += struct.pack("<II", len(binary), BIN_CHUNK) + binary
    path.write_bytes(bytes(out))


def externalise(document: dict, binary: bytes, image_name: str, uri: str) -> tuple[dict, bytes, bytes]:
    """Point one image at `uri`, drop its bufferView, and compact the binary chunk.

    ⚠️ Dropping a bufferView RENUMBERS EVERY LATER ONE. An accessor or another image holding
    a stale index reads someone else's bytes and the file still loads — silently wrong
    geometry, with no error anywhere. Every index is remapped here, and the caller re-checks
    the structural payload afterwards.
    """
    images = document.get("images", [])
    matches = [i for i in images if i.get("name") == image_name and "bufferView" in i]
    if len(matches) != 1:
        raise ValueError(f"expected one embedded image named {image_name!r}, found {len(matches)}")
    image = matches[0]
    dead = image.pop("bufferView")
    image.pop("mimeType", None)
    image["uri"] = uri

    views = document["bufferViews"]
    start = views[dead].get("byteOffset", 0)
    payload = binary[start:start + views[dead]["byteLength"]]

    keep = [v for index, v in enumerate(views) if index != dead]
    remap = {old: new for new, old in enumerate(i for i in range(len(views)) if i != dead)}

    # Rebuild the chunk in the surviving views' own order so every offset stays 4-aligned.
    rebuilt = bytearray()
    for view in keep:
        offset = view.get("byteOffset", 0)
        chunk = binary[offset:offset + view["byteLength"]]
        rebuilt += b"\x00" * ((4 - len(rebuilt) % 4) % 4)
        view["byteOffset"] = len(rebuilt)
        rebuilt += chunk
    document["bufferViews"] = keep
    document["buffers"][0]["byteLength"] = len(rebuilt)

    for accessor in document.get("accessors", []):
        if "bufferView" in accessor:
            accessor["bufferView"] = remap[accessor["bufferView"]]
        sparse = accessor.get("sparse")
        if sparse:
            for part in ("indices", "values"):
                sparse[part]["bufferView"] = remap[sparse[part]["bufferView"]]
    for other in document.get("images", []):
        if "bufferView" in other:
            other["bufferView"] = remap[other["bufferView"]]
    return document, bytes(rebuilt), payload


def structural(document: dict, binary: bytes) -> str:
    """A digest of the geometry as it will actually be READ, not as it is indexed.

    ⚠️ Comparing the raw JSON here is the wrong test and it fails a correct rewrite: dropping
    a bufferView legitimately renumbers every accessor that pointed past it. What must not
    change is the BYTES each accessor resolves to, so resolve them and digest those.
    """
    resolved = []
    for accessor in document.get("accessors", []):
        view = accessor.get("bufferView")
        blob = b""
        if view is not None:
            v = document["bufferViews"][view]
            start = v.get("byteOffset", 0) + accessor.get("byteOffset", 0)
            blob = binary[start:start + v["byteLength"] - accessor.get("byteOffset", 0)]
        resolved.append({
            "count": accessor.get("count"), "type": accessor.get("type"),
            "componentType": accessor.get("componentType"), "min": accessor.get("min"),
            "max": accessor.get("max"), "bytes": blob.hex(),
            "stride": document["bufferViews"][view].get("byteStride") if view is not None else None,
        })
    other = {k: document.get(k) for k in STRUCTURAL if k != "accessors"}
    return json.dumps({"accessors": resolved, **other}, sort_keys=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="report what would change and exit non-zero if anything would")
    args = parser.parse_args()

    pending: list[str] = []
    saved = 0

    for canonical, (sidecar, users) in FAMILIES.items():
        target = PROPS / canonical
        payloads: set[bytes] = set()
        for filename, image_name in users:
            path = PROPS / filename
            document, binary = read_glb(path)
            image = next((i for i in document.get("images", []) if i.get("name") == image_name), None)
            if image is None:
                raise SystemExit(f"{filename}: no image named {image_name!r}")
            if "uri" in image:
                continue  # already shared

            before_geometry = structural(document, binary)
            before_size = path.stat().st_size
            document, binary, payload = externalise(document, binary, image_name, canonical)
            if structural(document, binary) != before_geometry:
                raise SystemExit(f"{filename}: structural payload changed — refusing to write")
            payloads.add(payload)

            if args.check:
                pending.append(f"{filename}: {image_name} -> {canonical}")
                continue
            write_glb(path, document, binary)
            saved += before_size - path.stat().st_size
            print(f"  {filename}: {image_name} -> {canonical} "
                  f"({before_size / 1024:.0f}K -> {path.stat().st_size / 1024:.0f}K)")

        if not payloads or args.check:
            continue
        if len(payloads) != 1:
            raise SystemExit(f"{canonical}: users embed {len(payloads)} DIFFERENT images — not a family")
        blob = payloads.pop()
        # The decoded sidecar and the payload just pulled out must be the same bytes; assert it
        # rather than trust it, because a mismatch would silently retexture the whole family.
        promoted = PROPS / sidecar
        if promoted.exists() and promoted.read_bytes() != blob:
            raise SystemExit(f"{canonical}: sidecar {sidecar} differs from the embedded payload")
        target.write_bytes(blob)
        print(f"  wrote {canonical} ({target.stat().st_size / 1024:.0f}K, {len(users)} users)")

    for canonical, users in REPOINTED.items():
        if not (PROPS / canonical).exists():
            raise SystemExit(f"{canonical}: repoint target does not exist")
        for filename, image_name in users:
            path = PROPS / filename
            document, binary = read_glb(path)
            image = next((i for i in document.get("images", []) if i.get("name") == image_name), None)
            if image is None:
                raise SystemExit(f"{filename}: no image named {image_name!r}")
            if image.get("uri") == canonical:
                continue
            if "uri" in image:
                # Already external, just aimed at the wrong file: this is a one-line edit and
                # must NOT go through externalise(), which requires an embedded bufferView.
                stale = image["uri"]
                image["uri"] = canonical
                if args.check:
                    pending.append(f"{filename}: {stale} -> {canonical} (repointed)")
                    continue
                write_glb(path, document, binary)
                print(f"  {filename}: {stale} -> {canonical} (repointed)")
                continue
            before_geometry = structural(document, binary)
            before_size = path.stat().st_size
            document, binary, _ = externalise(document, binary, image_name, canonical)
            if structural(document, binary) != before_geometry:
                raise SystemExit(f"{filename}: structural payload changed — refusing to write")
            if args.check:
                pending.append(f"{filename}: {image_name} -> {canonical} (repointed)")
                continue
            write_glb(path, document, binary)
            saved += before_size - path.stat().st_size
            print(f"  {filename}: {image_name} -> {canonical} (repointed, payload discarded) "
                  f"({before_size / 1024:.0f}K -> {path.stat().st_size / 1024:.0f}K)")

    if args.check:
        for line in pending:
            print(f"WOULD SHARE {line}")
        return 1 if pending else 0

    # ⚠️ DELETING A SIDECAR IS ONLY SAFE ONCE NOTHING PRODUCES IT. These files are the IMPORTER'S
    # output, not adoption residue, so removing one while its GLB still embeds the image just
    # makes the next import write it back — and the "saving" evaporates between two runs.
    still_embedded = [
        path.name for path in sorted(PROPS.glob("*.glb"))
        if any("bufferView" in image for image in read_glb(path)[0].get("images", []))
    ]
    if still_embedded:
        raise SystemExit(
            "refusing to remove sidecars: these props still embed an image, so the importer "
            f"would extract it again — {', '.join(still_embedded)}")

    referenced = {
        image["uri"] for path in sorted(PROPS.glob("*.glb"))
        for image in read_glb(path)[0].get("images", []) if "uri" in image
    }
    referenced |= {
        image["uri"] for path in sorted(PROPS.glob("*.gltf"))
        for image in json.loads(path.read_text(encoding="utf-8")).get("images", []) if "uri" in image
    }
    dead = [p for p in sorted(PROPS.glob("*.png")) if p.name not in referenced]
    for path in dead:
        size = path.stat().st_size
        path.unlink()
        (PROPS / (path.name + ".import")).unlink(missing_ok=True)
        saved += size
        print(f"  removed unreferenced texture {path.name}")

    print(f"share_nature_textures: {saved / (1024 * 1024):.1f} MiB removed from the build")
    return 0


if __name__ == "__main__":
    sys.exit(main())
