#!/usr/bin/env python3
"""Verify card N9's scene surgery straight out of main.unity's YAML.

Scene YAML is plain text, so the state the player will actually get is
greppable without opening Unity. Run from the repo root:

    python3 tools/reticle_verify.py [--scene Assets/Scenes/main.unity]

Exits 0 and prints one PASS line per check, or exits 1 listing failures.
"""
import argparse
import re
import sys

CLS_GAMEOBJECT = "1"
CLS_TRANSFORM = "4"
CLS_CAMERA = "20"
CLS_MESHRENDERER = "23"
CLS_MONOBEHAVIOUR = "114"
CLS_RECTTRANSFORM = "224"
CLS_CANVAS = "223"
CLS_CANVASGROUP = "225"

RENDERMODE_OVERLAY = 0


def parse(path):
    """Split the scene into {fileID: (class, body)} plus a name index."""
    text = open(path, encoding="utf-8", errors="replace").read()
    docs = {}
    for chunk in text.split("\n--- "):
        m = re.match(r"!u!(\d+) &(\d+)", chunk)
        if not m:
            continue
        docs[m.group(2)] = (m.group(1), chunk)
    return docs


def field(body, name, default=None):
    m = re.search(r"^\s*%s: (.*)$" % re.escape(name), body, re.M)
    return m.group(1).strip() if m else default


def ref(body, name):
    m = re.search(r"%s: \{fileID: (-?\d+)" % re.escape(name), body)
    return m.group(1) if m else None


def components(body):
    return re.findall(r"component: \{fileID: (\d+)\}", body)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scene", default="Assets/Scenes/main.unity")
    args = ap.parse_args()

    docs = parse(args.scene)
    gos = {fid: b for fid, (c, b) in docs.items() if c == CLS_GAMEOBJECT}
    names = {fid: field(b, "m_Name", "?") for fid, b in gos.items()}
    by_name = {}
    for fid, n in names.items():
        by_name.setdefault(n, []).append(fid)

    # component class -> owning GameObject
    owner = {}
    for fid, (cls, body) in docs.items():
        go = ref(body, "m_GameObject")
        if go:
            owner[fid] = go

    fails = []
    passes = []

    def check(ok, msg):
        (passes if ok else fails).append(msg)

    # --- 1. exactly one Screen Space Overlay canvas -----------------------
    overlays = [
        fid for fid, (cls, body) in docs.items()
        if cls == CLS_CANVAS and int(field(body, "m_RenderMode", "0")) == RENDERMODE_OVERLAY
    ]
    check(len(overlays) == 1,
          "overlay canvases == 1 (found %d: %s)"
          % (len(overlays), [names.get(owner.get(f), "?") for f in overlays]))
    canvas_go = owner.get(overlays[0]) if overlays else None

    # --- 2. Reticle exists, centred 48x48, under that canvas --------------
    reticles = by_name.get("Reticle", [])
    check(len(reticles) == 1, "exactly one GameObject named 'Reticle' (found %d)" % len(reticles))
    if reticles:
        rgo = reticles[0]
        rts = [c for c in components(gos[rgo]) if docs.get(c, ("",))[0] == CLS_RECTTRANSFORM]
        check(bool(rts), "Reticle has a RectTransform")
        if rts:
            body = docs[rts[0]][1]
            check(field(body, "m_AnchorMin") == "{x: 0.5, y: 0.5}", "Reticle anchorMin 0.5/0.5")
            check(field(body, "m_AnchorMax") == "{x: 0.5, y: 0.5}", "Reticle anchorMax 0.5/0.5")
            check(field(body, "m_Pivot") == "{x: 0.5, y: 0.5}", "Reticle pivot 0.5/0.5")
            check(field(body, "m_AnchoredPosition") == "{x: 0, y: 0}", "Reticle anchoredPosition 0,0")
            check(field(body, "m_SizeDelta") == "{x: 48, y: 48}", "Reticle sizeDelta 48x48")
            father = ref(body, "m_Father")
            check(owner.get(father) == canvas_go,
                  "Reticle parented to the overlay canvas (parent=%s)"
                  % names.get(owner.get(father), "?"))
            # last sibling => deterministic draw order above the captions
            if father and father in docs:
                kids = re.findall(r"- \{fileID: (\d+)\}",
                                  re.search(r"m_Children:\n((?:\s+- \{fileID: \d+\}\n)*)",
                                            docs[father][1]).group(1))
                check(kids and kids[-1] == rts[0],
                      "Reticle is the last sibling of the canvas (index %s of %d)"
                      % (kids.index(rts[0]) if rts[0] in kids else "?", len(kids)))

        check(field(gos[rgo], "m_IsActive") == "1", "Reticle GameObject active")

        # CanvasGroup: idle alpha, no raycast blocking
        cgs = [c for c in components(gos[rgo]) if docs.get(c, ("",))[0] == CLS_CANVASGROUP]
        check(bool(cgs), "Reticle has a CanvasGroup")
        if cgs:
            b = docs[cgs[0]][1]
            check(field(b, "m_Alpha") == "0.5", "CanvasGroup idle alpha 0.5 (got %s)" % field(b, "m_Alpha"))
            check(field(b, "m_BlocksRaycasts") == "0", "CanvasGroup blocksRaycasts off")
            check(field(b, "m_Interactable") == "0", "CanvasGroup interactable off")

        # ReticleView with all four parts wired
        mbs = [c for c in components(gos[rgo]) if docs.get(c, ("",))[0] == CLS_MONOBEHAVIOUR]
        view = None
        for c in mbs:
            b = docs[c][1]
            if all(field(b, k) is not None for k in ("idleAlpha", "activeAlpha", "confirmFlash")):
                view = b
        check(view is not None, "Reticle has the ReticleView component")
        if view:
            for part in ("outline", "ring", "fill", "dot", "group"):
                r = ref(view, part)
                check(r not in (None, "0"), "ReticleView.%s wired (fileID %s)" % (part, r))
            check(field(view, "idleAlpha") == "0.5", "ReticleView idle alpha 0.5")
            check(field(view, "activeAlpha") == "1", "ReticleView in-range alpha 1.0")

        # four child Images
        for part in ("Outline", "Ring", "Fill", "Dot"):
            hits = [f for f in by_name.get(part, [])]
            check(bool(hits), "child Image '%s' exists" % part)

    # --- 3. legacy quads kept, renderers off ------------------------------
    for quad in ("Cam_Reticle", "Cam_Spinner_Reticle"):
        hits = by_name.get(quad, [])
        check(len(hits) == 1,
              "%s GameObject still present (Main.cs reads its transform)" % quad)
        if hits:
            mrs = [c for c in components(gos[hits[0]])
                   if docs.get(c, ("",))[0] == CLS_MESHRENDERER]
            check(bool(mrs), "%s has a MeshRenderer to mute" % quad)
            for mr in mrs:
                en = field(docs[mr][1], "m_Enabled")
                check(en == "0", "%s MeshRenderer m_Enabled: 0 (got %s)" % (quad, en))
            # Card's wording claims these are cameras; assert the measured truth.
            cams = [c for c in components(gos[hits[0]])
                    if docs.get(c, ("",))[0] == CLS_CAMERA]
            check(not cams,
                  "%s carries no Camera (it is a MeshRenderer quad; see tools/reticle.md)" % quad)

    # --- 4. camera inventory unchanged ------------------------------------
    cams = [(fid, b) for fid, (c, b) in docs.items() if c == CLS_CAMERA]
    enabled = [f for f, b in cams if field(b, "m_Enabled") == "1"
               and field(gos.get(owner.get(f), ""), "m_IsActive") == "1"]
    check(len(cams) == 9, "camera count still 9 (got %d)" % len(cams))
    check(len(enabled) == 5,
          "enabled+active cameras == 5 (got %d: %s)"
          % (len(enabled), sorted(names.get(owner.get(f), "?") for f in enabled)))

    for p in passes:
        print("PASS " + p)
    for f in fails:
        print("FAIL " + f)
    print("RETICLE_VERIFY pass=%d fail=%d" % (len(passes), len(fails)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
