#!/usr/bin/env python3
"""Static guard for card N12: no enabled legacy TextMesh may remain in the scene.

Parses Assets/Scenes/main.unity (and the AR label prefabs Main.cs instantiates)
as Unity YAML and fails if:

  1. any `--- !u!102` document (TextMesh, class id 102) is present whose owning
     GameObject is active and whose sibling MeshRenderer is enabled, or
  2. Main.cs still binds text through `TextMesh` instead of `TMP_Text`.

Disabled TextMesh components are tolerated (owner decision C7 is pending, so
the two empty fontSize-150 objects may be disabled rather than deleted) but are
reported so they do not go unnoticed.

Also reports the number of TextMeshPro components found (MonoBehaviour whose
m_Script GUID is TMP's TextMeshPro / TextMeshProUGUI) so the migration is
visibly present, not just the legacy component absent.

Exit 0 = clean, 1 = violation. No Unity needed, so it can run in CI.
"""

import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCENE = os.path.join(ROOT, "Assets", "Scenes", "main.unity")
PREFABS = [os.path.join(ROOT, "Assets", n) for n in
           ("ARLeftLabel.prefab", "ARRightLabel.prefab", "ARBHLabel.prefab")]
MAIN = os.path.join(ROOT, "Assets", "Scripts", "Main.cs")

# Script GUIDs from the com.unity.textmeshpro package (stable across versions).
TMP_GUIDS = {
    "9541d86e2fd84c1d9990edf0852d74ab": "TextMeshPro",       # 3D / world-space
    "f4688fdb7df04437aeb418b961361dc5": "TextMeshProUGUI",   # canvas
}

DOC_RE = re.compile(r"^--- !u!(\d+) &(\d+)( stripped)?\s*$", re.M)


def parse(path):
    """Return {fileID: (classID, body)} for a Unity YAML file."""
    with open(path, encoding="utf-8", errors="replace") as fh:
        text = fh.read()
    docs = {}
    heads = list(DOC_RE.finditer(text))
    for i, m in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(text)
        docs[m.group(2)] = (m.group(1), text[m.end():end])
    return docs


def field(body, name, default=None):
    m = re.search(r"^\s*%s: (.*)$" % re.escape(name), body, re.M)
    return m.group(1).strip() if m else default


def ref(body, name):
    m = re.search(r"^\s*%s: \{fileID: (-?\d+)" % re.escape(name), body, re.M)
    return m.group(1) if m else None


def guid_of(body, name):
    m = re.search(r"^\s*%s: \{fileID: -?\d+, guid: ([0-9a-f]{32})" % re.escape(name), body, re.M)
    return m.group(1) if m else None


def components(go_body):
    return re.findall(r"- component: \{fileID: (\d+)\}", go_body)


def check_file(path, failures, notes):
    label = os.path.relpath(path, ROOT)
    if not os.path.exists(path):
        failures.append("%s: missing" % label)
        return 0, 0
    docs = parse(path)

    tmp_count = 0
    legacy_enabled = 0
    legacy_disabled = 0

    for fid, (cls, body) in docs.items():
        if cls == "114":
            g = guid_of(body, "m_Script")
            if g in TMP_GUIDS:
                tmp_count += 1
        elif cls == "102":
            go_id = ref(body, "m_GameObject")
            go = docs.get(go_id)
            go_name = field(go[1], "m_Name", "?") if go else "?"
            active = field(go[1], "m_IsActive", "1") == "1" if go else True
            renderer_on = True
            if go:
                for cid in components(go[1]):
                    c = docs.get(cid)
                    if c and c[0] == "23":
                        renderer_on = field(c[1], "m_Enabled", "1") == "1"
            if active and renderer_on:
                legacy_enabled += 1
                failures.append("%s: enabled legacy TextMesh &%s on '%s' (text=%r)"
                                % (label, fid, go_name, field(body, "m_Text", "")))
            else:
                legacy_disabled += 1
                notes.append("%s: disabled legacy TextMesh &%s on '%s' (tolerated, C7 pending)"
                             % (label, fid, go_name))

    notes.append("%s: TextMeshPro components=%d, legacy TextMesh enabled=%d disabled=%d"
                 % (label, tmp_count, legacy_enabled, legacy_disabled))
    return tmp_count, legacy_enabled


def check_main(failures, notes):
    if not os.path.exists(MAIN):
        failures.append("Main.cs missing")
        return
    with open(MAIN, encoding="utf-8", errors="replace") as fh:
        src = fh.read()
    # Strip // comments so a historical mention does not trip the guard.
    code = re.sub(r"//.*", "", src)
    hits = [m.start() for m in re.finditer(r"\bTextMesh\b", code)]
    for pos in hits:
        line = code.count("\n", 0, pos) + 1
        failures.append("Main.cs:%d still references legacy TextMesh" % line)
    if "using TMPro;" not in code:
        failures.append("Main.cs: missing `using TMPro;`")
    binds = len(re.findall(r"GetComponent<TMP_Text>\(\)", code))
    if binds == 0:
        failures.append("Main.cs: no GetComponent<TMP_Text>() bindings found")
    notes.append("Main.cs: TMP_Text bindings=%d" % binds)


def main():
    failures, notes = [], []
    total_tmp = 0
    for p in [SCENE] + PREFABS:
        t, _ = check_file(p, failures, notes)
        total_tmp += t
    if total_tmp == 0:
        failures.append("no TextMeshPro components found anywhere; migration not applied")
    check_main(failures, notes)

    for n in notes:
        print("note:", n)
    if failures:
        for f in failures:
            print("FAIL:", f)
        print("check_textmesh: %d violation(s)" % len(failures))
        return 1
    print("check_textmesh: OK (TextMeshPro components=%d)" % total_tmp)
    return 0


if __name__ == "__main__":
    sys.exit(main())
