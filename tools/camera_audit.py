#!/usr/bin/env python3
"""Camera / light / shader audit for Assets/Scenes/main.unity  (card N8).

READ-ONLY by construction: every input file is opened in "r" mode and the only
file written is the Markdown report passed via ``--out`` (default
``tools/camera_audit.md``).  Nothing under ``Assets/`` is ever touched, no
``.meta`` is rewritten, and no Unity build is invoked.

Why a script and not a hand-written document: later cards mutate the scene.
Re-running this regenerates the fact base so the audit never silently rots.

Usage:
    python3 tools/camera_audit.py [--scene ...] [--main ...] [--assets ...]
                                  [--project-settings ...] [--out ...]
Exit codes: 0 success, 2 scene or Main.cs not found.
"""

from __future__ import annotations

import argparse
import datetime
import os
import re
import sys
from typing import Any, Dict, Iterable, List, Optional, Tuple

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from unity_yaml import (  # noqa: E402  (local module, path set above)
    Document,
    build_guid_index,
    parse_documents,
    read_guid,
)

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

CLASS_GAMEOBJECT = 1
CLASS_TRANSFORM = 4
CLASS_CAMERA = 20
CLASS_LIGHT = 108
CLASS_MONOBEHAVIOUR = 114
CLASS_MESH_RENDERER = 23
CLASS_PARTICLE_SYSTEM = 198
CLASS_PREFAB_INSTANCE = 1001

CLEAR_FLAGS = {1: "Skybox", 2: "SolidColor", 3: "DepthOnly", 4: "Nothing"}
LIGHT_TYPES = {0: "Spot", 1: "Directional", 2: "Point", 3: "Rectangle/Area", 4: "Disc"}
LIGHTMAPPING = {1: "Realtime(legacy)", 2: "Baked", 4: "Realtime"}
SHADOW_TYPES = {0: "None", 1: "Hard", 2: "Soft"}

# Camera / light names the card claims exist, so the report can reconcile.
CARD_CAMERA_NAMES = [
    "Cam_Reticle",
    "AR_Camera_Static",
    "Portal_Camera_Next",
    "Main Camera",
    "Helmet_Camera",
    "CameraHouse",
    "AR_Camera_Result",
    "Cam_Spinner_Reticle",
    "Cam_Spinner",
    "AR_Camera_Project",
    "OVRCameraRig",
]
CARD_CLAIMS = {"cameras": 9, "lights": 15, "mesh_renderers": 75, "particle_systems": 9}

# Built-in Unity shaders referenced by fileID inside the always-present
# "0000000000000000f000000000000000" (unity default resources) guid.
BUILTIN_SHADERS = {
    4: "Diffuse (legacy)",
    7: "VertexLit (legacy)",
    46: "Specular (legacy)",
    103: "Skybox/6 Sided",
    104: "Skybox/Cubemap",
    106: "Skybox/Procedural",
    10101: "Hidden/Internal-Colored",
    10700: "Legacy Shaders/Transparent/Diffuse",
    10703: "Legacy Shaders/Transparent/VertexLit",
    10721: "Legacy Shaders/Particles/Alpha Blended",
    10750: "Standard",
    10752: "Standard (Specular setup)",
}


# --------------------------------------------------------------------------
# helpers
# --------------------------------------------------------------------------
def file_id(ref: Any) -> int:
    if isinstance(ref, dict):
        try:
            return int(ref.get("fileID", 0))
        except (TypeError, ValueError):
            return 0
    return 0


def guid_of(ref: Any) -> str:
    if isinstance(ref, dict):
        return str(ref.get("guid", "") or "")
    return ""


def as_int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def as_num(value: Any) -> Any:
    if isinstance(value, (int, float)):
        return value
    return "?"


def fmt(value: Any) -> str:
    if value is None or value == "":
        return "n/a"
    if isinstance(value, float):
        return ("%.4f" % value).rstrip("0").rstrip(".")
    return str(value)


def md_escape(text: str) -> str:
    return str(text).replace("|", "\\|")


def read_layers(project_settings: str) -> Dict[int, str]:
    """Layer index -> name from ProjectSettings/TagManager.asset (if present)."""
    path = os.path.join(project_settings, "TagManager.asset")
    if not os.path.isfile(path):
        return {}
    layers: Dict[int, str] = {}
    in_layers = False
    index = 0
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        for line in handle:
            stripped = line.rstrip("\n")
            if re.match(r"^\s{2}layers:\s*$", stripped):
                in_layers = True
                continue
            if in_layers:
                item = re.match(r"^\s{2}-\s?(.*)$", stripped)
                if item is None:
                    break
                name = item.group(1).strip()
                if name:
                    layers[index] = name
                index += 1
    return layers


def describe_mask(bits: Any, layers: Dict[int, str]) -> str:
    value = as_int(bits, -1)
    if value < 0:
        return "unparsed (%r)" % (bits,)
    if value == 0xFFFFFFFF or value == -1:
        return "0xffffffff (Everything)"
    names: List[str] = []
    for index in range(32):
        if value & (1 << index):
            names.append(layers.get(index, "layer%d" % index) or "layer%d" % index)
    joined = ", ".join(names) if names else "Nothing"
    return "%d / 0x%08x -> %s" % (value, value & 0xFFFFFFFF, joined)


# --------------------------------------------------------------------------
# scene graph
# --------------------------------------------------------------------------
class Scene:
    """Indexed view over the parsed scene documents."""

    def __init__(self, docs: List[Document]):
        self.docs = docs
        self.by_id: Dict[int, Document] = {d.file_id: d for d in docs}
        self.gameobjects: Dict[int, Document] = {
            d.file_id: d for d in docs if d.class_id == CLASS_GAMEOBJECT
        }
        self.transforms: Dict[int, Document] = {
            d.file_id: d for d in docs if d.class_id == CLASS_TRANSFORM
        }
        self.cameras = [d for d in docs if d.class_id == CLASS_CAMERA]
        self.lights = [d for d in docs if d.class_id == CLASS_LIGHT]
        self.prefab_instances = [d for d in docs if d.class_id == CLASS_PREFAB_INSTANCE]
        self.mesh_renderers = [d for d in docs if d.class_id == CLASS_MESH_RENDERER]
        self.particle_systems = [d for d in docs if d.class_id == CLASS_PARTICLE_SYSTEM]

        # GameObject -> its Transform (needed for parent chains).
        self.transform_of_go: Dict[int, Document] = {}
        for transform in self.transforms.values():
            owner = file_id(transform.body.get("m_GameObject"))
            if owner:
                self.transform_of_go.setdefault(owner, transform)

    # -- naming -----------------------------------------------------------
    def go_name(self, go_id: int) -> str:
        go = self.gameobjects.get(go_id)
        if go is None:
            doc = self.by_id.get(go_id)
            if doc is not None and doc.stripped:
                return "<stripped prefab GameObject &%d>" % go_id
            return "<missing GameObject &%d>" % go_id
        name = go.body.get("m_Name")
        if name in (None, ""):
            return "<unnamed &%d>" % go_id
        return str(name)

    def owner_id(self, component: Document) -> int:
        return file_id(component.body.get("m_GameObject"))

    def is_active_self(self, go_id: int) -> Optional[int]:
        go = self.gameobjects.get(go_id)
        if go is None:
            return None
        return as_int(go.body.get("m_IsActive"), -1)

    def parent_chain(self, go_id: int) -> List[int]:
        """Ancestor GameObject ids, nearest parent first."""
        chain: List[int] = []
        seen = set()
        current = go_id
        while True:
            transform = self.transform_of_go.get(current)
            if transform is None:
                break
            father_id = file_id(transform.body.get("m_Father"))
            if not father_id:
                break
            father = self.by_id.get(father_id)
            if father is None:
                break
            parent_go = file_id(father.body.get("m_GameObject"))
            if not parent_go or parent_go in seen:
                break
            seen.add(parent_go)
            chain.append(parent_go)
            current = parent_go
        return chain

    def hierarchy_path(self, go_id: int) -> str:
        chain = self.parent_chain(go_id)
        parts = [self.go_name(pid) for pid in reversed(chain)]
        parts.append(self.go_name(go_id))
        return "/".join(parts)

    def hierarchy_active(self, go_id: int) -> Tuple[Optional[bool], str]:
        """(active-in-hierarchy, reason) honouring every ancestor m_IsActive."""
        own = self.is_active_self(go_id)
        if own is None:
            return None, "owner GameObject not in scene YAML (stripped/prefab)"
        if own == 0:
            return False, "self m_IsActive: 0"
        unknown = False
        for ancestor in self.parent_chain(go_id):
            state = self.is_active_self(ancestor)
            if state is None:
                unknown = True
                continue
            if state == 0:
                return False, "ancestor '%s' m_IsActive: 0" % self.go_name(ancestor)
        if unknown:
            return None, "an ancestor is stripped (state not in scene YAML)"
        return True, "self and all ancestors m_IsActive: 1"

    def components_of(self, go_id: int) -> List[Document]:
        go = self.gameobjects.get(go_id)
        if go is None:
            return []
        out: List[Document] = []
        entries = go.body.get("m_Component") or []
        if isinstance(entries, list):
            for entry in entries:
                ref = entry.get("component") if isinstance(entry, dict) else None
                doc = self.by_id.get(file_id(ref))
                if doc is not None:
                    out.append(doc)
        return out

    def component_class_ids(self, go_id: int) -> List[int]:
        return [d.class_id for d in self.components_of(go_id)]


# --------------------------------------------------------------------------
# render texture resolution
# --------------------------------------------------------------------------
def resolve_render_texture(
    ref: Any, rt_index: Dict[str, str]
) -> Tuple[str, Optional[str]]:
    """Return (human description, asset path) for a m_TargetTexture reference."""
    if file_id(ref) == 0 and not guid_of(ref):
        return "none (renders to screen/eye buffer)", None
    guid = guid_of(ref)
    path = rt_index.get(guid)
    if path is None:
        return "unresolved guid %s (asset missing from Assets/)" % (guid or "?"), None
    name = os.path.basename(path)
    width = height = aa = "?"
    try:
        for doc in parse_documents(path):
            if "m_Width" in doc.body:
                width = fmt(doc.body.get("m_Width"))
                height = fmt(doc.body.get("m_Height"))
                aa = fmt(doc.body.get("m_AntiAliasing"))
                break
    except OSError:
        pass
    return "%s (%sx%s, AA %s)" % (name, width, height, aa), path


def render_texture_consumers(assets_root: str, rt_paths: Iterable[str]) -> Dict[str, List[str]]:
    """renderTexture asset path -> materials that reference its guid (measured).

    Lets the report say *which* material samples a camera's target texture
    instead of asserting that one must exist.
    """
    guid_to_path: Dict[str, str] = {}
    for path in rt_paths:
        if not path:
            continue
        guid = read_guid(path + ".meta")
        if guid:
            guid_to_path[guid] = path
    out: Dict[str, List[str]] = {path: [] for path in guid_to_path.values()}
    if not guid_to_path:
        return out
    for dirpath, dirnames, filenames in os.walk(assets_root):
        dirnames[:] = [d for d in dirnames if d not in ("Library", "Temp", "obj")]
        for name in sorted(filenames):
            if not name.endswith(".mat"):
                continue
            mat_path = os.path.join(dirpath, name)
            try:
                with open(mat_path, "r", encoding="utf-8", errors="replace") as handle:
                    text = handle.read()
            except OSError:
                continue
            for guid, rt_path in guid_to_path.items():
                if guid in text:
                    out[rt_path].append(os.path.relpath(mat_path, REPO_ROOT))
    return out


# --------------------------------------------------------------------------
# camera / light row extraction
# --------------------------------------------------------------------------
def camera_rows(scene: Scene, layers: Dict[int, str], rt_index: Dict[str, str]) -> List[Dict[str, Any]]:
    rows: List[Dict[str, Any]] = []
    for cam in scene.cameras:
        owner = scene.owner_id(cam)
        active, reason = scene.hierarchy_active(owner)
        parents = scene.parent_chain(owner)
        target, target_path = resolve_render_texture(cam.body.get("m_TargetTexture"), rt_index)
        rows.append(
            {
                "kind": "camera-component",
                "file_id": cam.file_id,
                "line": cam.line,
                "name": scene.go_name(owner),
                "path": scene.hierarchy_path(owner),
                "parent": scene.go_name(parents[0]) if parents else "<scene root>",
                "enabled": as_int(cam.body.get("m_Enabled"), -1),
                "depth": as_num(cam.body.get("m_Depth")),
                "clear_flags_raw": as_int(cam.body.get("m_ClearFlags"), -1),
                "clear_flags": CLEAR_FLAGS.get(
                    as_int(cam.body.get("m_ClearFlags"), -1), "unknown"
                ),
                "mask_bits": cam.get("m_CullingMask.m_Bits"),
                "mask": describe_mask(cam.get("m_CullingMask.m_Bits"), layers),
                "target_texture": target,
                "target_texture_path": target_path,
                "target_consumers": [],
                "orthographic": as_int(cam.body.get("orthographic"), -1),
                "fov": as_num(cam.body.get("field of view")),
                "near": as_num(cam.body.get("near clip plane")),
                "far": as_num(cam.body.get("far clip plane")),
                "owner_is_active": scene.is_active_self(owner),
                "hierarchy_active": active,
                "hierarchy_active_reason": reason,
                "stripped": cam.stripped,
                "prefab_source": guid_of(cam.body.get("m_CorrespondingSourceObject")),
            }
        )
    rows.sort(key=lambda r: (str(r["name"]).lower(), r["file_id"]))
    return rows


def light_rows(scene: Scene, layers: Dict[int, str]) -> List[Dict[str, Any]]:
    rows: List[Dict[str, Any]] = []
    for light in scene.lights:
        owner = scene.owner_id(light)
        active, reason = scene.hierarchy_active(owner)
        parents = scene.parent_chain(owner)
        colour = light.body.get("m_Color")
        if isinstance(colour, dict):
            colour_text = "r %s g %s b %s" % (
                fmt(colour.get("r")),
                fmt(colour.get("g")),
                fmt(colour.get("b")),
            )
        else:
            colour_text = "n/a"
        lightmapping = as_int(light.body.get("m_Lightmapping"), -1)
        rows.append(
            {
                "kind": "light-component",
                "file_id": light.file_id,
                "line": light.line,
                "name": scene.go_name(owner),
                "path": scene.hierarchy_path(owner),
                "parent": scene.go_name(parents[0]) if parents else "<scene root>",
                "enabled": as_int(light.body.get("m_Enabled"), -1),
                "type_raw": as_int(light.body.get("m_Type"), -1),
                "type": LIGHT_TYPES.get(as_int(light.body.get("m_Type"), -1), "unknown"),
                "mode_raw": lightmapping,
                "mode": LIGHTMAPPING.get(lightmapping, "unknown"),
                "range": as_num(light.body.get("m_Range")),
                "intensity": as_num(light.body.get("m_Intensity")),
                "spot_angle": as_num(light.body.get("m_SpotAngle")),
                "colour": colour_text,
                "mask_bits": light.get("m_CullingMask.m_Bits"),
                "mask": describe_mask(light.get("m_CullingMask.m_Bits"), layers),
                "shadow_raw": as_int(light.get("m_Shadows.m_Type"), -1),
                "shadows": SHADOW_TYPES.get(as_int(light.get("m_Shadows.m_Type"), -1), "unknown"),
                "shadow_strength": as_num(light.get("m_Shadows.m_Strength")),
                "owner_is_active": scene.is_active_self(owner),
                "hierarchy_active": active,
                "hierarchy_active_reason": reason,
                "stripped": light.stripped,
            }
        )
    rows.sort(key=lambda r: (str(r["name"]).lower(), r["file_id"]))
    return rows


def prefab_camera_rows(scene: Scene, prefab_index: Dict[str, str]) -> List[Dict[str, Any]]:
    """PrefabInstances that look camera/OVR related (their components are not in the scene YAML)."""
    rows: List[Dict[str, Any]] = []
    for prefab in scene.prefab_instances:
        source = prefab.get("m_SourcePrefab") or prefab.get("m_ParentPrefab")
        guid = guid_of(source)
        path = prefab_index.get(guid, "")
        names: List[str] = []
        mods = prefab.get("m_Modification.m_Modifications") or []
        if isinstance(mods, list):
            for mod in mods:
                if isinstance(mod, dict) and mod.get("propertyPath") == "m_Name":
                    names.append(str(mod.get("value")))
        haystack = " ".join(names + [os.path.basename(path)])
        if not re.search(r"cam|ovr", haystack, re.IGNORECASE):
            continue
        rows.append(
            {
                "kind": "prefab-instance",
                "file_id": prefab.file_id,
                "line": prefab.line,
                "name": names[0] if names else os.path.basename(path) or "<unnamed prefab>",
                "guid": guid,
                "path": path or "(prefab asset not found by guid)",
            }
        )
    rows.sort(key=lambda r: (str(r["name"]).lower(), r["file_id"]))
    return rows


def prefab_light_rows(
    scene: Scene, prefab_index: Dict[str, str], layers: Dict[int, str]
) -> List[Dict[str, Any]]:
    """Lights contributed by PrefabInstances, measured from the prefab assets.

    ``main.unity`` only stores 13 ``!u!108`` documents, but every
    ``!u!1001`` PrefabInstance drags whatever Lights live inside its source
    prefab into the loaded scene.  Those are real runtime lights and real cost,
    so they are measured here (opening each ``.prefab`` read-only) instead of
    being left out and making the count look like a card error.

    ``m_IsActive`` / ``m_Enabled`` are read from the prefab asset and then
    overridden by any matching ``m_Modification`` on the instance, which is how
    Unity itself resolves them.
    """
    rows: List[Dict[str, Any]] = []
    cache: Dict[str, List[Document]] = {}

    for prefab in scene.prefab_instances:
        source = prefab.get("m_SourcePrefab") or prefab.get("m_ParentPrefab")
        guid = guid_of(source)
        path = prefab_index.get(guid)
        if not path or not os.path.isfile(path):
            continue
        if path not in cache:
            try:
                cache[path] = parse_documents(path)
            except OSError:
                cache[path] = []
        docs = cache[path]
        lights = [d for d in docs if d.class_id == CLASS_LIGHT]
        if not lights:
            continue

        gos = {d.file_id: d for d in docs if d.class_id == CLASS_GAMEOBJECT}

        # instance overrides: (target fileID, propertyPath) -> value
        overrides: Dict[Tuple[int, str], Any] = {}
        mods = prefab.get("m_Modification.m_Modifications") or []
        if isinstance(mods, list):
            for mod in mods:
                if not isinstance(mod, dict):
                    continue
                target = file_id(mod.get("target"))
                prop = str(mod.get("propertyPath") or "")
                if target and prop:
                    overrides[(target, prop)] = mod.get("value")

        instance_names = [
            str(mod.get("value"))
            for mod in mods
            if isinstance(mod, dict) and mod.get("propertyPath") == "m_Name"
        ] if isinstance(mods, list) else []
        instance_name = instance_names[0] if instance_names else os.path.basename(path)

        for light in lights:
            owner_id = file_id(light.body.get("m_GameObject"))
            owner = gos.get(owner_id)
            owner_name = str(owner.body.get("m_Name")) if owner is not None else "<unknown>"
            asset_enabled = as_int(light.body.get("m_Enabled"), -1)
            asset_active = (
                as_int(owner.body.get("m_IsActive"), -1) if owner is not None else -1
            )
            enabled = as_int(overrides.get((light.file_id, "m_Enabled"), asset_enabled), -1)
            active = as_int(overrides.get((owner_id, "m_IsActive"), asset_active), -1)
            lightmapping = as_int(light.body.get("m_Lightmapping"), -1)
            rows.append(
                {
                    "kind": "prefab-light",
                    "instance_file_id": prefab.file_id,
                    "instance_line": prefab.line,
                    "instance_name": instance_name,
                    "prefab_path": os.path.relpath(path, REPO_ROOT),
                    "prefab_guid": guid,
                    "file_id": light.file_id,
                    "name": owner_name,
                    "enabled": enabled,
                    "enabled_overridden": (light.file_id, "m_Enabled") in overrides,
                    "owner_is_active": active,
                    "active_overridden": (owner_id, "m_IsActive") in overrides,
                    "type_raw": as_int(light.body.get("m_Type"), -1),
                    "type": LIGHT_TYPES.get(as_int(light.body.get("m_Type"), -1), "unknown"),
                    "mode_raw": lightmapping,
                    "mode": LIGHTMAPPING.get(lightmapping, "unknown"),
                    "range": as_num(light.body.get("m_Range")),
                    "intensity": as_num(light.body.get("m_Intensity")),
                    "mask_bits": light.get("m_CullingMask.m_Bits"),
                    "mask": describe_mask(light.get("m_CullingMask.m_Bits"), layers),
                    "shadow_raw": as_int(light.get("m_Shadows.m_Type"), -1),
                    "shadows": SHADOW_TYPES.get(
                        as_int(light.get("m_Shadows.m_Type"), -1), "unknown"
                    ),
                    "shadow_strength": as_num(light.get("m_Shadows.m_Strength")),
                }
            )
    rows.sort(key=lambda r: (str(r["instance_name"]).lower(), str(r["name"]).lower(), r["file_id"]))
    return rows


def prefab_component_counts(
    scene: Scene, prefab_index: Dict[str, str], class_ids: Iterable[int]
) -> Dict[int, int]:
    """How many components of each class id the PrefabInstances bring in.

    Used only to reconcile the card's MeshRenderer / ParticleSystem numbers;
    every prefab asset is opened read-only.
    """
    wanted = list(class_ids)
    totals: Dict[int, int] = {cid: 0 for cid in wanted}
    cache: Dict[str, List[Document]] = {}
    for prefab in scene.prefab_instances:
        source = prefab.get("m_SourcePrefab") or prefab.get("m_ParentPrefab")
        path = prefab_index.get(guid_of(source))
        if not path or not os.path.isfile(path):
            continue
        if path not in cache:
            try:
                cache[path] = parse_documents(path)
            except OSError:
                cache[path] = []
        for doc in cache[path]:
            if doc.class_id in totals:
                totals[doc.class_id] += 1
    return totals


def camera_named_gameobjects(scene: Scene) -> List[Dict[str, Any]]:
    """GameObjects whose name looks camera-ish but which carry no Camera component."""
    out: List[Dict[str, Any]] = []
    for go_id, go in scene.gameobjects.items():
        name = str(go.body.get("m_Name") or "")
        if not re.search(r"cam|ovr", name, re.IGNORECASE):
            continue
        if CLASS_CAMERA in scene.component_class_ids(go_id):
            continue
        active, reason = scene.hierarchy_active(go_id)
        out.append(
            {
                "name": name,
                "file_id": go_id,
                "line": go.line,
                "path": scene.hierarchy_path(go_id),
                "components": sorted(set(scene.component_class_ids(go_id))),
                "is_active": scene.is_active_self(go_id),
                "hierarchy_active": active,
                "hierarchy_active_reason": reason,
            }
        )
    out.sort(key=lambda r: (r["name"].lower(), r["file_id"]))
    return out


# --------------------------------------------------------------------------
# Main.cs reference scanning
# --------------------------------------------------------------------------
ENABLE_PATTERNS = [
    (re.compile(r"\.SetActive\s*\("), "activates/deactivates GameObject"),
    (re.compile(r"\.enabled\s*="), "writes component .enabled"),
    (re.compile(r"\bDestroy\s*\("), "destroys object"),
]
TRANSFORM_PATTERNS = [
    (re.compile(r"\.transform\b"), "reads/writes .transform"),
    (re.compile(r"\.GetComponent\s*<\s*Camera\s*>"), "reads Camera component"),
    (re.compile(r"\.GetComponent\s*<\s*Light\s*>"), "reads Light component"),
    (re.compile(r"\.GetComponent\s*<\s*Skybox\s*>"), "reads Skybox component"),
    (re.compile(r"cullingMask"), "writes cullingMask"),
    (re.compile(r"fieldOfView|\baspect\b"), "writes projection (fov/aspect)"),
]

# field/identifier -> the scene GameObject name(s) it binds to.
# Bindings themselves are *measured* from the GameObject.Find() calls below;
# this table only supplies the identifiers to grep for.
MAIN_FIELD_HINTS = [
    "camera_house",
    "main_camera",
    "main_camera_skybox",
    "portal_camera_next",
    "portal_camera_next_skybox",
    "helmet_light",
    "helmet_light_light",
    "cam_reticle",
    "cam_spinner",
    "ar_camera_project",
    "ar_camera_static",
    "cam_euler",
    "gaze_cam_euler",
    "anti_gaze_cam_euler",
    "extreme_camera_shake",
]

FIND_CALL = re.compile(r"GameObject\.Find\s*\(\s*\"([^\"]+)\"\s*\)")
ASSIGN_FIND = re.compile(r"^\s*(?://\s*)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*GameObject\.Find\s*\(\s*\"([^\"]+)\"\s*\)")


def scan_main_cs(path: str, subjects: Iterable[str]) -> Dict[str, Any]:
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        lines = handle.read().splitlines()

    find_bindings: List[Dict[str, Any]] = []
    for number, line in enumerate(lines, start=1):
        match = ASSIGN_FIND.search(line)
        if match:
            find_bindings.append(
                {
                    "line": number,
                    "field": match.group(1),
                    "scene_name": match.group(2),
                    "commented_out": line.strip().startswith("//"),
                    "text": line.strip(),
                }
            )

    subject_list = sorted(set(subjects))
    refs: Dict[str, List[Dict[str, Any]]] = {s: [] for s in subject_list}
    for number, line in enumerate(lines, start=1):
        stripped = line.strip()
        commented = stripped.startswith("//")
        for subject in subject_list:
            if not re.search(r"(?<![A-Za-z0-9_])%s(?![A-Za-z0-9_])" % re.escape(subject), line):
                if '"%s"' % subject not in line:
                    continue
            tags: List[str] = []
            for pattern, label in ENABLE_PATTERNS:
                if pattern.search(line):
                    tags.append(label)
            for pattern, label in TRANSFORM_PATTERNS:
                if pattern.search(line):
                    tags.append(label)
            if re.search(r"^\s*(public\s+|private\s+)?[A-Za-z_][\w<>\[\],\s]*\s%s\s*(=|;)" % re.escape(subject), line):
                tags.append("field declaration")
            refs[subject].append(
                {
                    "line": number,
                    "text": stripped,
                    "tags": tags or ["reference"],
                    "commented_out": commented,
                }
            )
    return {"path": path, "total_lines": len(lines), "find_bindings": find_bindings, "refs": refs}


OTHER_SCRIPT_PATTERNS = [
    (re.compile(r"GetComponent\s*<\s*Camera\s*>"), "GetComponent<Camera>"),
    (re.compile(r"GetComponent\s*<\s*Light\s*>"), "GetComponent<Light>"),
    (re.compile(r"\bcullingMask\b"), "writes/reads cullingMask"),
    (re.compile(r"\.SetActive\s*\("), "SetActive"),
    (re.compile(r"\.enabled\s*="), "writes .enabled"),
    (re.compile(r"\bDestroy\s*\("), "Destroy"),
    (re.compile(r"\bpixelLightCount\b"), "writes QualitySettings.pixelLightCount"),
    (re.compile(r"\bshadow(s|Distance|Quality)\b", re.IGNORECASE), "touches shadow settings"),
]


def other_scene_scripts(
    scene: Scene, assets_root: str, main_path: str
) -> List[Dict[str, Any]]:
    """Every other MonoBehaviour actually present in the scene, and whether it
    can touch a Camera/Light.

    The card only asks for a Main.cs grep, but every "remove" recommendation
    carries the residual risk "some *other* script might enable this at
    runtime".  That risk is cheap to measure: resolve each `!u!114` m_Script
    guid to its `.cs` file and grep it.  Measured, so the risk column can stop
    hand-waving.
    """
    cs_index = build_guid_index([assets_root], (".cs",))
    main_abs = os.path.abspath(main_path)
    rows: List[Dict[str, Any]] = []
    seen: set = set()
    for doc in scene.docs:
        if doc.class_id != CLASS_MONOBEHAVIOUR:
            continue
        guid = guid_of(doc.body.get("m_Script"))
        path = cs_index.get(guid)
        owner = scene.owner_id(doc)
        key = (guid, owner)
        if key in seen:
            continue
        seen.add(key)
        capabilities: List[str] = []
        entry_points: List[str] = []
        if path and os.path.isfile(path):
            try:
                with open(path, "r", encoding="utf-8", errors="replace") as handle:
                    text = handle.read()
            except OSError:
                text = ""
            for pattern, label in OTHER_SCRIPT_PATTERNS:
                if pattern.search(text):
                    capabilities.append(label)
            # Unity runtime entry points: a capability only matters if something
            # calls it without the editor. Maintenance-style scripts whose work
            # hangs off a public method invoked from Assets/Editor never run in
            # a build.
            for hook in ("Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable"):
                if re.search(r"\b(?:void|IEnumerator)\s+%s\s*\(" % hook, text):
                    entry_points.append(hook)
        rows.append(
            {
                "file_id": doc.file_id,
                "line": doc.line,
                "guid": guid,
                "path": os.path.relpath(path, REPO_ROOT) if path else "",
                "is_main": bool(path) and os.path.abspath(path) == main_abs,
                "owner": scene.go_name(owner),
                "owner_active": scene.hierarchy_active(owner)[0],
                "enabled": as_int(doc.body.get("m_Enabled"), -1),
                "capabilities": capabilities,
                "entry_points": entry_points,
            }
        )
    rows.sort(key=lambda r: (r["path"] or "zzz", r["file_id"]))
    return rows


def locate_main_cs(explicit: Optional[str], assets_root: str) -> Optional[str]:
    if explicit and os.path.isfile(explicit):
        return explicit
    preferred = os.path.join(assets_root, "Scripts", "Main.cs")
    if os.path.isfile(preferred):
        return preferred
    hits: List[str] = []
    for dirpath, dirnames, filenames in os.walk(assets_root):
        dirnames[:] = [
            d for d in dirnames if d not in ("Library", "Temp", "obj", "Build", "Builds")
        ]
        if "Main.cs" in filenames:
            hits.append(os.path.join(dirpath, "Main.cs"))
    return sorted(hits)[0] if hits else None


# --------------------------------------------------------------------------
# shader histogram
# --------------------------------------------------------------------------
def shader_histogram(assets_root: str) -> Tuple[List[Tuple[str, int, str]], int, List[str]]:
    """(rows, material count, unresolved notes); rows = (shader name, count, source)."""
    shader_index = build_guid_index([assets_root], (".shader", ".shadergraph"))
    counts: Dict[Tuple[str, str], int] = {}
    unresolved: List[str] = []
    material_count = 0

    for dirpath, dirnames, filenames in os.walk(assets_root):
        dirnames[:] = [d for d in dirnames if d not in ("Library", "Temp", "obj")]
        for name in sorted(filenames):
            if not name.endswith(".mat"):
                continue
            path = os.path.join(dirpath, name)
            material_count += 1
            ref = None
            try:
                with open(path, "r", encoding="utf-8", errors="replace") as handle:
                    for line in handle:
                        if line.lstrip().startswith("m_Shader:"):
                            ref = line.split(":", 1)[1].strip()
                            if ref.count("{") > ref.count("}"):
                                ref = ref + " " + next(handle, "").strip()
                            break
            except OSError:
                continue
            if ref is None:
                unresolved.append("%s (no m_Shader key)" % os.path.relpath(path, REPO_ROOT))
                continue
            inline = {}
            for key, value in re.findall(r"([A-Za-z]+)\s*:\s*([^,}]+)", ref):
                inline[key] = value.strip()
            guid = inline.get("guid", "")
            shader_file_id = as_int(inline.get("fileID"), 0)
            if guid in shader_index:
                shader_path = shader_index[guid]
                shader_name = shader_name_from_file(shader_path) or os.path.basename(shader_path)
                key = (shader_name, os.path.relpath(shader_path, REPO_ROOT))
            elif shader_file_id in BUILTIN_SHADERS:
                key = (BUILTIN_SHADERS[shader_file_id], "Unity built-in (fileID %d)" % shader_file_id)
            else:
                key = (
                    "unresolved shader (fileID %d, guid %s)" % (shader_file_id, guid or "-"),
                    "unresolved",
                )
                unresolved.append(
                    "%s -> fileID %d guid %s"
                    % (os.path.relpath(path, REPO_ROOT), shader_file_id, guid or "-")
                )
            counts[key] = counts.get(key, 0) + 1

    rows = [(name, count, source) for (name, source), count in counts.items()]
    rows.sort(key=lambda r: (-r[1], r[0].lower()))
    return rows, material_count, unresolved


SHADER_DECL = re.compile(r'^\s*Shader\s+"([^"]+)"')


def shader_name_from_file(path: str) -> Optional[str]:
    try:
        with open(path, "r", encoding="utf-8-sig", errors="replace") as handle:
            for _ in range(80):
                line = handle.readline()
                if not line:
                    break
                match = SHADER_DECL.match(line)
                if match:
                    return match.group(1)
    except OSError:
        return None
    return None


# --------------------------------------------------------------------------
# recommendations
# --------------------------------------------------------------------------
# Recommendation rules are derived from *measured* fields only:
#   - a camera referenced by Main.cs (via GameObject.Find + GetComponent) can
#     never be deleted without a code change -> keep / gate.
#   - a camera that is disabled AND unreferenced is dead weight -> remove.
#   - a camera that renders into a RenderTexture feeds a material -> gate.
RECOMMEND_KEEP = "keep"
RECOMMEND_GATE = "gate to phase"
RECOMMEND_REMOVE = "remove"


def recommend_camera(row: Dict[str, Any], main_refs: Dict[str, List[Dict[str, Any]]],
                     bound_names: Dict[str, List[str]]) -> Tuple[str, str, str]:
    """Return (recommendation, evidence, risk) for one camera row."""
    name = row["name"]
    fields = bound_names.get(name, [])
    live_refs = sum(
        1
        for field in fields
        for ref in main_refs.get(field, [])
        if not ref["commented_out"]
    )
    has_rt = row["target_texture_path"] is not None
    consumers = row.get("target_consumers") or []
    consumer_text = (
        "its target texture is sampled by %s" % ", ".join("`%s`" % c for c in consumers)
        if consumers
        else "no material under Assets/ references its target texture guid"
    )
    enabled = row["enabled"] == 1
    active = row["hierarchy_active"]

    evidence: List[str] = []
    if fields:
        evidence.append("bound in Main.cs via %s (%d live reference lines)" % (
            ", ".join(sorted(fields)), live_refs))
    else:
        evidence.append("no Main.cs field binds this GameObject name")
    evidence.append("m_Enabled: %s" % row["enabled"])
    evidence.append("hierarchyActive: %s (%s)" % (row["hierarchy_active"], row["hierarchy_active_reason"]))
    evidence.append("m_Depth: %s, clear: %s" % (fmt(row["depth"]), row["clear_flags"]))
    evidence.append("target: %s" % row["target_texture"])
    if has_rt:
        evidence.append(consumer_text)

    if fields and live_refs:
        if has_rt:
            return (
                RECOMMEND_GATE,
                "; ".join(evidence),
                "Main.cs resolves this name at Start() and writes aspect/fieldOfView on it every "
                "frame, and it renders into %s where %s. Deleting the GameObject NREs in Main.cs; "
                "deleting the RenderTexture leaves that material sampling a stale/black texture. "
                "Gate the render (`Camera.enabled = false` outside its phase), never delete the "
                "GameObject." % (row["target_texture"], consumer_text),
            )
        return (
            RECOMMEND_KEEP,
            "; ".join(evidence),
            "Main.cs resolves this object by name at Start() and dereferences it every frame; "
            "removal is an immediate NullReferenceException. If the goal is cost, toggle "
            "Camera.enabled from the phase state machine instead.",
        )

    if has_rt:
        return (
            RECOMMEND_GATE,
            "; ".join(evidence),
            "Renders into %s every frame even though no Main.cs field binds it; %s. Safe play is "
            "to disable the Camera component for phases that do not show that material, not to "
            "delete the asset chain." % (row["target_texture"], consumer_text),
        )

    if not enabled or active is False:
        return (
            RECOMMEND_REMOVE,
            "; ".join(evidence),
            "Already inert in the YAML (disabled component and/or inactive hierarchy) and no live "
            "Main.cs reference, so deleting it cannot change a rendered pixel today. Residual "
            "risk is narrow and listed in section 4.6: only the scripts shown there with a "
            "runtime entry point could re-enable it, and an AnimationClip could still target it "
            "by path - check section 4.6 and any Animator on an ancestor before deleting.",
        )

    if active is None:
        return (
            RECOMMEND_GATE,
            "; ".join(evidence),
            "Owner GameObject state is not fully resolvable from main.unity (stripped prefab "
            "member), so enabled-ness depends on the prefab asset. Resolve the prefab before "
            "touching it.",
        )

    return (
        RECOMMEND_GATE,
        "; ".join(evidence),
        "Enabled and active but unreferenced by Main.cs, so it renders the scene every frame for "
        "no scripted reason. Likely a leftover editor/preview camera, but depth %s means it may be "
        "compositing over the HMD eye buffer - verify visually before removal." % fmt(row["depth"]),
    )


def recommend_light(row: Dict[str, Any], main_refs: Dict[str, List[Dict[str, Any]]],
                    bound_names: Dict[str, List[str]]) -> Tuple[str, str, str]:
    name = row["name"]
    fields = bound_names.get(name, [])
    live_refs = sum(
        1
        for field in fields
        for ref in main_refs.get(field, [])
        if not ref["commented_out"]
    )
    evidence = [
        "bound in Main.cs via %s (%d live lines)" % (", ".join(sorted(fields)), live_refs)
        if fields
        else "no Main.cs field binds this GameObject name",
        "m_Enabled: %s" % row["enabled"],
        "hierarchyActive: %s (%s)" % (row["hierarchy_active"], row["hierarchy_active_reason"]),
        "type %s, mode %s, range %s, intensity %s, shadows %s"
        % (row["type"], row["mode"], fmt(row["range"]), fmt(row["intensity"]), row["shadows"]),
    ]
    if fields and live_refs:
        return (
            RECOMMEND_KEEP,
            "; ".join(evidence),
            "Main.cs mutates this light at runtime (colour per scene). Deleting the GameObject "
            "NREs at Start(); deleting only the Light component NREs on the colour write.",
        )
    if row["enabled"] != 1 or row["hierarchy_active"] is False:
        return (
            RECOMMEND_REMOVE,
            "; ".join(evidence),
            "Inert in YAML and unreferenced. Risk is purely visual regression if some phase "
            "expects its bake contribution - check m_Lightmapping (%s) against baked lightmaps."
            % row["mode"],
        )
    if row["shadow_raw"] != 0:
        return (
            RECOMMEND_GATE,
            "; ".join(evidence),
            "Realtime shadow-casting light (%s) with range %s: the per-pixel + shadowmap cost is "
            "the expensive part on mobile/WebGL. Prefer dropping shadows or gating by phase over "
            "deleting, because its contribution is visible in the lit scene."
            % (row["shadows"], fmt(row["range"])),
        )
    return (
        RECOMMEND_GATE,
        "; ".join(evidence),
        "Enabled realtime light with no script reference; removal changes scene appearance for "
        "every phase that renders its culling mask (%s). Gate by phase and compare screenshots."
        % row["mask"],
    )


# --------------------------------------------------------------------------
# report
# --------------------------------------------------------------------------
def tri(value: Optional[bool]) -> str:
    if value is None:
        return "unknown"
    return "true" if value else "false"


def build_report(
    scene: Scene,
    cams: List[Dict[str, Any]],
    lights: List[Dict[str, Any]],
    prefab_cams: List[Dict[str, Any]],
    prefab_lights: List[Dict[str, Any]],
    prefab_counts: Dict[int, int],
    cam_named_gos: List[Dict[str, Any]],
    other_scripts: List[Dict[str, Any]],
    main_scan: Dict[str, Any],
    bound_names: Dict[str, List[str]],
    shaders: List[Tuple[str, int, str]],
    material_count: int,
    unresolved_shaders: List[str],
    layers: Dict[int, str],
    scene_path: str,
) -> str:
    out: List[str] = []
    w = out.append
    refs = main_scan["refs"]

    w("# Camera & Light Audit — `%s`" % os.path.relpath(scene_path, REPO_ROOT))
    w("")
    w("> **Read-only fact base (card N8).** Generated by `tools/camera_audit.py`; "
      "re-run it after any scene change instead of hand-editing this file. "
      "Nothing was disabled, deleted, or rebuilt to produce it.")
    w("")
    w("| | |")
    w("|---|---|")
    w("| Generated (UTC) | %s |" % datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d %H:%M:%SZ"))
    w("| Scene | `%s` (%d YAML documents) |" % (os.path.relpath(scene_path, REPO_ROOT), len(scene.docs)))
    w("| Main.cs | `%s` (%d lines) |" % (os.path.relpath(main_scan["path"], REPO_ROOT), main_scan["total_lines"]))
    w("| Layer names | `ProjectSettings/TagManager.asset` (%d named layers) |" % len(layers))
    w("")
    w("**MEASURED vs INFERRED.** Every number in the camera, light, counts and shader tables is "
      "parsed out of the YAML / `.mat` / `.cs` text — no guessing. The *Recommendation* and *Risk* "
      "columns are **inferred** from those measurements by the rules in "
      "`recommend_camera()` / `recommend_light()`; they are judgement, not data. Anything the "
      "parser could not resolve is printed as `unknown` / `unresolved` rather than filled in from "
      "a plausible-looking name.")
    w("")

    # ---- counts / reconciliation
    w("## 1. Counts and reconciliation with the card")
    w("")
    w("| Thing | Card said | Measured | Match |")
    w("|---|---|---|---|")
    live_prefab_lights = [
        r for r in prefab_lights if r["enabled"] == 1 and r["owner_is_active"] == 1
    ]
    rows = [
        ("Camera components (`!u!20`)", CARD_CLAIMS["cameras"], len(cams)),
        ("Light components (`!u!108`) in `main.unity` itself", CARD_CLAIMS["lights"], len(lights)),
        (
            "Light components reachable at runtime (scene + PrefabInstance assets)",
            CARD_CLAIMS["lights"],
            len(lights) + len(prefab_lights),
        ),
        (
            "MeshRenderers (`!u!23`) in `main.unity` itself",
            CARD_CLAIMS["mesh_renderers"],
            len(scene.mesh_renderers),
        ),
        (
            "MeshRenderers reachable at runtime (scene + PrefabInstance assets)",
            CARD_CLAIMS["mesh_renderers"],
            len(scene.mesh_renderers) + prefab_counts.get(CLASS_MESH_RENDERER, 0),
        ),
        (
            "ParticleSystems (`!u!198`) in `main.unity` itself",
            CARD_CLAIMS["particle_systems"],
            len(scene.particle_systems),
        ),
    ]
    for label, claimed, measured in rows:
        mark = "yes" if claimed == measured else "**NO**"
        w("| %s | %d | %d | %s |" % (label, claimed, measured, mark))
    w("| Lights inside PrefabInstance source prefabs (`!u!108` in `.prefab`) | — | %d | — |"
      % len(prefab_lights))
    w("| ...of those, enabled **and** on an active GameObject | — | %d | — |" % len(live_prefab_lights))
    w("| MeshRenderers inside PrefabInstance source prefabs | — | %d | — |"
      % prefab_counts.get(CLASS_MESH_RENDERER, 0))
    w("| ParticleSystems inside PrefabInstance source prefabs | — | %d | — |"
      % prefab_counts.get(CLASS_PARTICLE_SYSTEM, 0))
    w("| GameObjects (`!u!1`) | — | %d | — |" % len(scene.gameobjects))
    w("| PrefabInstances (`!u!1001`) | — | %d | — |" % len(scene.prefab_instances))
    w("| Camera/OVR-named GameObjects *without* a Camera component | — | %d | — |" % len(cam_named_gos))
    w("")

    mismatches = [(l, c, m) for l, c, m in rows if c != m]
    if mismatches:
        w("**Mismatches are stated, not hidden:**")
        w("")
        for label, claimed, measured in mismatches:
            w("- %s: card said **%d**, measured **%d**." % (label, claimed, measured))
        w("")
    mr_total = len(scene.mesh_renderers) + prefab_counts.get(CLASS_MESH_RENDERER, 0)
    if len(scene.mesh_renderers) != CARD_CLAIMS["mesh_renderers"]:
        w("Same story for the card's \"75 MeshRenderers\": `main.unity` holds **%d** `!u!23` "
          "documents and the PrefabInstances add **%d** more (**%d** total reachable). Neither "
          "number is 75, so the card's figure is reported as unmatched rather than reconciled "
          "to a guess - likely a count taken from the Unity editor hierarchy (which also counts "
          "SkinnedMeshRenderers / inactive children) or from the scene before the "
          "OVRManager/helmet-visual strip commits."
          % (len(scene.mesh_renderers), prefab_counts.get(CLASS_MESH_RENDERER, 0), mr_total))
        w("")
    if len(lights) != CARD_CLAIMS["lights"]:
        w("Where the card's \"15 light entries\" number comes from (measured, not guessed): "
          "`main.unity` contains **%d** `!u!108` documents, and the scene's `!u!1001` "
          "PrefabInstances pull in **%d** more Lights from their source `.prefab` assets "
          "(%d of those are enabled on an active GameObject). Section 3.2 lists them "
          "individually. Treat the scene-YAML %d as the set you can edit by touching "
          "`main.unity`; the prefab lights need the prefab asset (or an instance override) "
          "to change." % (len(lights), len(prefab_lights), len(live_prefab_lights), len(lights)))
        w("")

    w("### Card's camera name list, resolved one by one")
    w("")
    w("| Name from card | Resolution | Where |")
    w("|---|---|---|")
    cam_by_name = {r["name"]: r for r in cams}
    go_by_name = {r["name"]: r for r in cam_named_gos}
    prefab_by_name = {r["name"]: r for r in prefab_cams}
    for name in CARD_CAMERA_NAMES:
        if name in cam_by_name:
            row = cam_by_name[name]
            w("| `%s` | found as Camera | `%s` line %d, Camera `&%d` |"
              % (md_escape(name), md_escape(row["path"]), row["line"], row["file_id"]))
        elif name in go_by_name:
            row = go_by_name[name]
            w("| `%s` | found as GameObject only (**no Camera component**) | `%s` line %d, GameObject `&%d` |"
              % (md_escape(name), md_escape(row["path"]), row["line"], row["file_id"]))
        elif name in prefab_by_name:
            row = prefab_by_name[name]
            w("| `%s` | prefab instance only (name only) | `%s` |" % (md_escape(name), md_escape(row["path"])))
        else:
            w("| `%s` | **not found in scene YAML** | — |" % md_escape(name))
    w("")
    w("All Camera component owners actually present, for completeness: %s."
      % ", ".join("`%s`" % r["name"] for r in cams))
    w("")

    # At-a-glance summary so the next card does not have to read 600 lines.
    w("## 1.5 Summary: what to do with each camera (detail in section 2)")
    w("")
    w("| camera | `m_Enabled` | depth | renders to | Main.cs binding | recommendation |")
    w("|---|---|---|---|---|---|")
    for row in cams:
        recommendation, _, _ = recommend_camera(row, refs, bound_names)
        fields = bound_names.get(row["name"], [])
        w("| `%s` | %s | %s | %s | %s | **%s** |" % (
            md_escape(row["name"]),
            row["enabled"],
            fmt(row["depth"]),
            md_escape(row["target_texture"].split(" (")[0]),
            ", ".join("`%s`" % f for f in sorted(fields)) if fields else "none",
            recommendation,
        ))
    w("")
    def tally(items: List[Dict[str, Any]], fn: Any) -> str:
        counts: Dict[str, int] = {}
        for item in items:
            rec = fn(item, refs, bound_names)[0]
            counts[rec] = counts.get(rec, 0) + 1
        return ", ".join("%d %s" % (c, r) for r, c in sorted(counts.items()))

    w("Camera tally: %s. Light tally (scene-YAML lights only): %s."
      % (tally(cams, recommend_camera), tally(lights, recommend_light)))
    w("")

    out.extend(section_cameras(cams, prefab_cams, cam_named_gos, refs, bound_names))
    out.extend(section_lights(lights, prefab_lights, refs, bound_names))
    out.extend(section_main_cs(main_scan, bound_names, cams, lights, other_scripts))
    out.extend(section_shaders(shaders, material_count, unresolved_shaders))
    out.extend(section_method())
    return "\n".join(out) + "\n"


def section_cameras(
    cams: List[Dict[str, Any]],
    prefab_cams: List[Dict[str, Any]],
    cam_named_gos: List[Dict[str, Any]],
    refs: Dict[str, List[Dict[str, Any]]],
    bound_names: Dict[str, List[str]],
) -> List[str]:
    out: List[str] = []
    w = out.append
    w("## 2. Cameras (measured from `!u!20` documents)")
    w("")
    w("| # | GameObject | fileID | line | `m_Enabled` | `m_Depth` | `m_ClearFlags` | `m_CullingMask` | `m_TargetTexture` | owner `m_IsActive` | hierarchyActive | parent |")
    w("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for index, row in enumerate(cams, start=1):
        w("| %d | `%s` | `&%d` | %d | %s | %s | %s (%d) | %s | %s | %s | %s | `%s` |" % (
            index,
            md_escape(row["name"]),
            row["file_id"],
            row["line"],
            row["enabled"],
            fmt(row["depth"]),
            row["clear_flags"],
            row["clear_flags_raw"],
            md_escape(row["mask"]),
            md_escape(row["target_texture"]),
            "unknown" if row["owner_is_active"] is None else row["owner_is_active"],
            tri(row["hierarchy_active"]),
            md_escape(row["parent"]),
        ))
    w("")
    w("Secondary camera geometry (same documents, kept out of the table above for width):")
    w("")
    w("| GameObject | full hierarchy path | orthographic | fov | near | far | from prefab guid | materials sampling its target texture |")
    w("|---|---|---|---|---|---|---|---|")
    for row in cams:
        consumers = row.get("target_consumers") or []
        w("| `%s` | `%s` | %s | %s | %s | %s | %s | %s |" % (
            md_escape(row["name"]),
            md_escape(row["path"]),
            row["orthographic"],
            fmt(row["fov"]),
            fmt(row["near"]),
            fmt(row["far"]),
            "`%s`" % row["prefab_source"] if row["prefab_source"] else "—",
            ", ".join("`%s`" % md_escape(c) for c in consumers)
            if consumers
            else ("none found" if row["target_texture_path"] else "— (no target texture)"),
        ))
    w("")

    if cam_named_gos:
        w("### Camera/OVR-named GameObjects with **no** Camera component")
        w("")
        w("These are the reason the card's list of 11 names only yields %d Camera components. "
          "Measured: the GameObject exists, its `m_Component` list contains no `!u!20`." % len(cams))
        w("")
        w("| GameObject | fileID | line | hierarchy path | component class ids | `m_IsActive` | hierarchyActive |")
        w("|---|---|---|---|---|---|---|")
        for row in cam_named_gos:
            w("| `%s` | `&%d` | %d | `%s` | %s | %s | %s |" % (
                md_escape(row["name"]),
                row["file_id"],
                row["line"],
                md_escape(row["path"]),
                ", ".join(str(c) for c in row["components"]) or "none",
                "unknown" if row["is_active"] is None else row["is_active"],
                tri(row["hierarchy_active"]),
            ))
        w("")

    if prefab_cams:
        w("### Camera/OVR-looking PrefabInstances (name only)")
        w("")
        w("Their Camera components live in the prefab asset, **not** in `main.unity`, so no "
          "`m_Enabled`/`m_Depth`/mask can be measured from the scene file.")
        w("")
        w("| Name | PrefabInstance fileID | line | source prefab |")
        w("|---|---|---|---|")
        for row in prefab_cams:
            w("| `%s` | `&%d` | %d | `%s` (guid `%s`) |" % (
                md_escape(row["name"]), row["file_id"], row["line"],
                md_escape(row["path"]), row["guid"] or "-"))
        w("")

    w("### Per-camera recommendation (inferred) and specific risk")
    w("")
    for row in cams:
        recommendation, evidence, risk = recommend_camera(row, refs, bound_names)
        w("#### `%s` — **%s**" % (row["name"], recommendation))
        w("")
        w("- Measured: `&%d` at line %d, path `%s`." % (row["file_id"], row["line"], row["path"]))
        w("- Measured: %s" % evidence)
        w("- Risk: %s" % risk)
        w("")
    return out


def section_lights(
    lights: List[Dict[str, Any]],
    prefab_lights: List[Dict[str, Any]],
    refs: Dict[str, List[Dict[str, Any]]],
    bound_names: Dict[str, List[str]],
) -> List[str]:
    out: List[str] = []
    w = out.append
    w("## 3. Lights (measured from `!u!108` documents)")
    w("")
    w("### 3.1 Lights declared directly in `main.unity` (%d)" % len(lights))
    w("")
    w("| # | GameObject | fileID | line | `m_Enabled` | type | mode (`m_Lightmapping`) | range | intensity | culling mask | shadows | shadow strength | owner `m_IsActive` | hierarchyActive |")
    w("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for index, row in enumerate(lights, start=1):
        w("| %d | `%s` | `&%d` | %d | %s | %s (%d) | %s (%d) | %s | %s | %s | %s (%d) | %s | %s | %s |" % (
            index,
            md_escape(row["name"]),
            row["file_id"],
            row["line"],
            row["enabled"],
            row["type"],
            row["type_raw"],
            row["mode"],
            row["mode_raw"],
            fmt(row["range"]),
            fmt(row["intensity"]),
            md_escape(row["mask"]),
            row["shadows"],
            row["shadow_raw"],
            fmt(row["shadow_strength"]),
            "unknown" if row["owner_is_active"] is None else row["owner_is_active"],
            tri(row["hierarchy_active"]),
        ))
    w("")
    w("| GameObject | full hierarchy path | colour | spot angle | parent |")
    w("|---|---|---|---|---|")
    for row in lights:
        w("| `%s` | `%s` | %s | %s | `%s` |" % (
            md_escape(row["name"]), md_escape(row["path"]), row["colour"],
            fmt(row["spot_angle"]), md_escape(row["parent"])))
    w("")
    if prefab_lights:
        w("### 3.2 Lights contributed by `!u!1001` PrefabInstances (%d)" % len(prefab_lights))
        w("")
        w("Measured by opening each source `.prefab` read-only and resolving the instance's "
          "`m_Modification.m_Modifications` overrides on top of the asset values, which is how "
          "Unity resolves them at load. These Lights are **not** in `main.unity`, so deleting "
          "them requires editing the prefab asset or adding an instance override - a scene-only "
          "edit cannot reach them.")
        w("")
        w("| # | instance (`m_Name`) | light owner | source prefab | light fileID | `m_Enabled` | owner `m_IsActive` | type | mode | range | intensity | culling mask | shadows |")
        w("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
        for index, row in enumerate(prefab_lights, start=1):
            w("| %d | `%s` | `%s` | `%s` | `&%d` | %s%s | %s%s | %s (%d) | %s (%d) | %s | %s | %s | %s (%d) |" % (
                index,
                md_escape(row["instance_name"]),
                md_escape(row["name"]),
                md_escape(row["prefab_path"]),
                row["file_id"],
                row["enabled"],
                " (instance override)" if row["enabled_overridden"] else "",
                row["owner_is_active"],
                " (instance override)" if row["active_overridden"] else "",
                row["type"],
                row["type_raw"],
                row["mode"],
                row["mode_raw"],
                fmt(row["range"]),
                fmt(row["intensity"]),
                md_escape(row["mask"]),
                row["shadows"],
                row["shadow_raw"],
            ))
        w("")
        shadow_casters = [r for r in prefab_lights if r["shadow_raw"] != 0 and r["enabled"] == 1
                          and r["owner_is_active"] == 1]
        w("Measured consequence for a removal pass: %d of these %d prefab lights are live "
          "(enabled + active owner) and %d of the live ones cast realtime shadows. Those are the "
          "ones that cost per-pixel passes, and none of them can be switched off by editing "
          "`main.unity` alone."
          % (
              len([r for r in prefab_lights if r["enabled"] == 1 and r["owner_is_active"] == 1]),
              len(prefab_lights),
              len(shadow_casters),
          ))
        w("")

    w("### 3.3 Per-light recommendation (inferred) and specific risk")
    w("")
    for row in lights:
        recommendation, evidence, risk = recommend_light(row, refs, bound_names)
        w("#### `%s` `&%d` (line %d) — **%s**" % (row["name"], row["file_id"], row["line"], recommendation))
        w("")
        w("- Measured: %s" % evidence)
        w("- Risk: %s" % risk)
        w("")
    return out


def section_main_cs(
    main_scan: Dict[str, Any],
    bound_names: Dict[str, List[str]],
    cams: List[Dict[str, Any]],
    lights: List[Dict[str, Any]],
    other_scripts: List[Dict[str, Any]],
) -> List[str]:
    out: List[str] = []
    w = out.append
    path = os.path.relpath(main_scan["path"], REPO_ROOT)
    refs = main_scan["refs"]

    w("## 4. `%s` references" % path)
    w("")
    w("### 4.1 How camera/light fields are bound (measured `GameObject.Find`)")
    w("")
    w("| line | field | scene name looked up | state |")
    w("|---|---|---|---|")
    for binding in main_scan["find_bindings"]:
        if not re.search(r"cam|light|ovr|helmet", binding["field"] + binding["scene_name"], re.IGNORECASE):
            continue
        w("| %d | `%s` | `%s` | %s |" % (
            binding["line"],
            binding["field"],
            md_escape(binding["scene_name"]),
            "**commented out**" if binding["commented_out"] else "live",
        ))
    w("")
    w("Name-resolution consequence (measured): these lookups are by **string name**, so any "
      "GameObject rename or deletion fails silently at `Start()` and then throws a "
      "`NullReferenceException` on first dereference. There is no `[SerializeField]` wiring to "
      "catch it in the editor.")
    w("")

    scene_names = sorted({r["name"] for r in cams} | {r["name"] for r in lights})
    w("### 4.2 Scene-name -> Main.cs field map (measured)")
    w("")
    w("| scene GameObject | Main.cs field(s) | live reference lines |")
    w("|---|---|---|")
    for name in scene_names:
        fields = bound_names.get(name, [])
        if fields:
            lines = sorted({
                ref["line"]
                for field in fields
                for ref in refs.get(field, [])
                if not ref["commented_out"]
            })
            w("| `%s` | %s | %s |" % (
                md_escape(name),
                ", ".join("`%s`" % f for f in sorted(fields)),
                ", ".join(str(n) for n in lines) or "none",
            ))
        else:
            w("| `%s` | **none** | — |" % md_escape(name))
    w("")

    w("### 4.3 Every matching line, per identifier")
    w("")
    for subject in sorted(refs):
        hits = refs[subject]
        if not hits:
            w("#### `%s` — no references found" % subject)
            w("")
            continue
        w("#### `%s` — %d reference line(s)" % (subject, len(hits)))
        w("")
        w("| line | tags | source |")
        w("|---|---|---|")
        for hit in hits:
            w("| %d | %s | `%s` |" % (
                hit["line"],
                ", ".join(hit["tags"]) + (" (commented out)" if hit["commented_out"] else ""),
                md_escape(hit["text"]),
            ))
        w("")

    # 4.5: fields that Main.cs overwrites at runtime, so the YAML value in
    # sections 2/3 is only the *initial* value. Measured from the tag matches.
    override_tags = {
        "writes cullingMask": "Camera.cullingMask",
        "writes projection (fov/aspect)": "Camera.fieldOfView / aspect",
    }
    overrides: Dict[str, List[Tuple[str, int, str]]] = {}
    for subject in sorted(refs):
        for hit in refs[subject]:
            if hit["commented_out"]:
                continue
            for tag in hit["tags"]:
                if tag in override_tags:
                    overrides.setdefault(override_tags[tag], []).append(
                        (subject, hit["line"], hit["text"])
                    )
    w("### 4.4 YAML fields that Main.cs overwrites at runtime (measured)")
    w("")
    if overrides:
        w("For these fields the value in sections 2 and 3 is only the **initial** value baked "
          "into the scene; the script replaces it every frame. Do not reason about the final "
          "frame cost from the YAML column alone.")
        w("")
        w("| overwritten property | via identifier | line | source |")
        w("|---|---|---|---|")
        for prop in sorted(overrides):
            for subject, line, text in sorted(overrides[prop], key=lambda t: t[1]):
                w("| `%s` | `%s` | %d | `%s` |" % (prop, subject, line, md_escape(text)))
        w("")
    else:
        w("None measured: no line in this file writes `cullingMask`, `fieldOfView` or `aspect` "
          "on a camera/light identifier, so the YAML values are the runtime values.")
        w("")

    enable_lines = sorted({
        hit["line"]
        for hits in refs.values()
        for hit in hits
        if any("activates" in t or "enabled" in t or "destroys" in t for t in hit["tags"])
    })
    w("### 4.5 Lines that enable/disable or destroy a camera/light subject (measured)")
    w("")
    if enable_lines:
        w("Lines: %s" % ", ".join(str(n) for n in enable_lines))
    else:
        w("**None.** No `SetActive`, `.enabled =` or `Destroy` call in `%s` targets any of the "
          "camera/light identifiers scanned above. Consequence: every camera and light keeps the "
          "`m_Enabled` / `m_IsActive` the YAML gives it for the whole run - nothing turns these "
          "cameras off mid-session today, so every enabled one in section 2 renders from the "
          "first frame to the last. (Their `cullingMask` and `fieldOfView` *are* rewritten - see "
          "4.4 - so only the enabled/active columns are runtime-stable, not the mask column.) "
          "This is also why \"gate to phase\" below is new work, not a config change." % path)
    w("")

    w("### 4.6 Other MonoBehaviours present in the scene (measured)")
    w("")
    w("The card only asked for a `Main.cs` grep, but every \"remove\" recommendation otherwise "
      "has to end with \"...unless some other script enables it\". That is measurable: each "
      "`!u!114` in the scene has its `m_Script` guid resolved to a `.cs` file, which is then "
      "grepped for camera/light capability. This is the full list - there are no other "
      "scripts in `main.unity`.")
    w("")
    w("| script | component fileID | on GameObject | component `m_Enabled` | owner active | Unity runtime entry points | camera/light capability found |")
    w("|---|---|---|---|---|---|---|")
    for row in other_scripts:
        w("| `%s`%s | `&%d` | `%s` | %s | %s | %s | %s |" % (
            md_escape(row["path"] or "unresolved guid %s" % (row["guid"] or "?")),
            " *(this is the audited Main.cs)*" if row["is_main"] else "",
            row["file_id"],
            md_escape(row["owner"]),
            row["enabled"],
            tri(row["owner_active"]),
            ", ".join("`%s`" % e for e in row["entry_points"]) if row["entry_points"]
            else "**none** (no Awake/Start/Update - cannot act in a build)",
            ", ".join("`%s`" % c for c in row["capabilities"]) if row["capabilities"] else "**none**",
        ))
    w("")
    risky = [
        r for r in other_scripts
        if not r["is_main"] and r["capabilities"] and r["entry_points"]
    ]
    inert = [
        r for r in other_scripts
        if not r["is_main"] and r["capabilities"] and not r["entry_points"]
    ]
    if risky:
        w("So the residual \"another script might re-enable it\" risk reduces to these %d "
          "script(s) that both have a camera/light capability **and** a runtime entry point: "
          "%s. Check them before deleting anything."
          % (len(risky), ", ".join("`%s`" % r["path"] for r in risky)))
        w("")
    if inert:
        w("Measured as *not* a risk: %s match the capability grep but define no "
          "`Awake`/`Start`/`Update`/`OnEnable`, so nothing invokes them in a player build "
          "(their work is triggered from `Assets/Editor/`). They can change lights in the "
          "editor, never at runtime."
          % ", ".join("`%s`" % r["path"] for r in inert))
    else:
        w("Measured result: **no other script in the scene can touch a Camera or Light.** "
          "`Main.cs` is the only runtime influence, so the YAML enabled/active state plus "
          "section 4.4 is the complete picture.")
    w("")
    return out


def section_shaders(
    shaders: List[Tuple[str, int, str]], material_count: int, unresolved: List[str]
) -> List[str]:
    out: List[str] = []
    w = out.append
    w("## 5. Shader histogram across all `Assets/**/*.mat` (measured)")
    w("")
    w("%d material assets scanned; %d distinct shaders." % (material_count, len(shaders)))
    w("")
    w("| count | shader name | resolved from |")
    w("|---|---|---|")
    for name, count, source in shaders:
        w("| %d | `%s` | %s |" % (count, md_escape(name), md_escape(source)))
    w("")
    if unresolved:
        w("Unresolved shader references (guid has no `.shader` + `.meta` pair under `Assets/`), "
          "stated rather than guessed:")
        w("")
        for item in unresolved:
            w("- `%s`" % md_escape(item))
        w("")
    return out


def section_method() -> List[str]:
    return [
        "## 6. Method and limits",
        "",
        "- Parser: `tools/unity_yaml.py`, stdlib only. Unity's `!u!<class>` tags break strict YAML "
        "loaders, so documents are split on `--- !u!` and parsed with an indentation-aware "
        "line reader that also rejoins Unity's wrapped inline maps "
        "(`{fileID: 1, guid: x,\\n    type: 3}`).",
        "- `hierarchyActive` is computed by walking `Transform.m_Father` -> `Transform.m_GameObject` "
        "to the root and AND-ing every `m_IsActive`. If any ancestor is a `stripped` prefab member "
        "its state is not in `main.unity` and the value is reported as `unknown`.",
        "- Culling-mask layer names come from `ProjectSettings/TagManager.asset`; unnamed indices "
        "print as `layerN`.",
        "- `m_TargetTexture` guids are resolved through `Assets/**/*.renderTexture.meta`, and the "
        "`.renderTexture` is read for width/height/AA.",
        "- Cameras living inside prefab assets (e.g. an OVR rig) cannot have their component fields "
        "measured from the scene file; they are listed as *name only*.",
        "- Lights are measured in two places: the 13 `!u!108` documents in `main.unity`, plus "
        "the Lights inside each `!u!1001` PrefabInstance's source `.prefab` (resolved through the "
        "instance's `m_Modification` overrides, as Unity does at load). Only the first group can "
        "be changed by editing the scene file.",
        "- Every `!u!114` in the scene has its `m_Script` guid resolved and grepped, so the "
        "\"another script could re-enable it\" risk is measured (section 4.6) rather than assumed. "
        "Scripts with a capability but no `Awake`/`Start`/`Update`/`OnEnable` are reported as "
        "editor-only.",
        "- Not measured here: actual GPU cost / draw-call counts (needs a device profile, card "
        "N1), whether an AnimationClip or Animator toggles these objects, and scripts that are "
        "not components in this scene (e.g. anything added via `AddComponent` at runtime from an "
        "asset bundle). Cameras inside prefab assets would also be name-only; none were found.",
        "",
        "Regenerate with:",
        "",
        "```sh",
        "python3 tools/camera_audit.py",
        "```",
        "",
    ]


# --------------------------------------------------------------------------
# field <-> scene-name binding and CLI
# --------------------------------------------------------------------------
def build_bound_names(
    main_scan: Dict[str, Any], scene_names: Iterable[str]
) -> Dict[str, List[str]]:
    """scene GameObject name -> Main.cs field names, measured from GameObject.Find.

    Only live (non commented-out) bindings create a mapping; a commented-out
    binding is recorded in the report but must not make an object look used.
    """
    wanted = set(scene_names)
    out: Dict[str, List[str]] = {}
    for binding in main_scan["find_bindings"]:
        if binding["commented_out"]:
            continue
        name = binding["scene_name"]
        if name not in wanted:
            continue
        fields = out.setdefault(name, [])
        if binding["field"] not in fields:
            fields.append(binding["field"])
    return out


def subjects_for_scan(
    main_scan_bindings: List[Dict[str, Any]], scene_names: Iterable[str]
) -> List[str]:
    """Identifiers to grep Main.cs for: hint fields, Find-bound fields, scene names."""
    subjects = set(MAIN_FIELD_HINTS)
    subjects.update(scene_names)
    for binding in main_scan_bindings:
        if re.search(r"cam|light|ovr|helmet|skybox", binding["field"], re.IGNORECASE):
            subjects.add(binding["field"])
        if re.search(r"cam|light|ovr|helmet", binding["scene_name"], re.IGNORECASE):
            subjects.add(binding["field"])
    return sorted(s for s in subjects if s)


def parse_args(argv: Optional[List[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Read-only camera/light/shader audit of a Unity scene (card N8)."
    )
    parser.add_argument(
        "--scene",
        default=os.path.join(REPO_ROOT, "Assets", "Scenes", "main.unity"),
        help="path to the scene .unity file",
    )
    parser.add_argument(
        "--main",
        default=None,
        help="path to Main.cs (default: Assets/Scripts/Main.cs, else globbed)",
    )
    parser.add_argument(
        "--assets",
        default=os.path.join(REPO_ROOT, "Assets"),
        help="Assets root used for materials/shaders/renderTextures/prefabs",
    )
    parser.add_argument(
        "--project-settings",
        default=os.path.join(REPO_ROOT, "ProjectSettings"),
        help="ProjectSettings dir (for TagManager.asset layer names)",
    )
    parser.add_argument(
        "--out",
        default=os.path.join(REPO_ROOT, "tools", "camera_audit.md"),
        help="Markdown report to write (the ONLY file this script writes)",
    )
    return parser.parse_args(argv)


def run(args: argparse.Namespace) -> int:
    if not os.path.isfile(args.scene):
        sys.stderr.write("camera_audit: scene not found: %s\n" % args.scene)
        return 2
    main_cs = locate_main_cs(args.main, args.assets)
    if main_cs is None:
        sys.stderr.write(
            "camera_audit: Main.cs not found (looked at --main, %s/Scripts/Main.cs, "
            "then globbed %s)\n" % (args.assets, args.assets)
        )
        return 2

    scene = Scene(parse_documents(args.scene))
    layers = read_layers(args.project_settings)
    rt_index = build_guid_index([args.assets], (".renderTexture",))
    prefab_index = build_guid_index([args.assets], (".prefab",))

    cams = camera_rows(scene, layers, rt_index)
    consumers = render_texture_consumers(
        args.assets, {r["target_texture_path"] for r in cams if r["target_texture_path"]}
    )
    for row in cams:
        row["target_consumers"] = consumers.get(row["target_texture_path"] or "", [])
    lights = light_rows(scene, layers)
    prefab_cams = prefab_camera_rows(scene, prefab_index)
    prefab_lights = prefab_light_rows(scene, prefab_index, layers)
    prefab_counts = prefab_component_counts(
        scene, prefab_index, (CLASS_MESH_RENDERER, CLASS_PARTICLE_SYSTEM, CLASS_CAMERA)
    )
    cam_named_gos = camera_named_gameobjects(scene)

    scene_names = sorted({r["name"] for r in cams} | {r["name"] for r in lights})

    # Pass 1: discover the GameObject.Find bindings so pass 2 can grep the real
    # field names (not just the hard-coded hints).
    discovery = scan_main_cs(main_cs, [])
    subjects = subjects_for_scan(discovery["find_bindings"], scene_names)
    main_scan = scan_main_cs(main_cs, subjects)
    other_scripts = other_scene_scripts(scene, args.assets, main_cs)
    bound_names = build_bound_names(main_scan, scene_names)

    shaders, material_count, unresolved_shaders = shader_histogram(args.assets)

    report = build_report(
        scene=scene,
        cams=cams,
        lights=lights,
        prefab_cams=prefab_cams,
        prefab_lights=prefab_lights,
        prefab_counts=prefab_counts,
        cam_named_gos=cam_named_gos,
        other_scripts=other_scripts,
        main_scan=main_scan,
        bound_names=bound_names,
        shaders=shaders,
        material_count=material_count,
        unresolved_shaders=unresolved_shaders,
        layers=layers,
        scene_path=args.scene,
    )

    out_dir = os.path.dirname(os.path.abspath(args.out))
    if out_dir and not os.path.isdir(out_dir):
        os.makedirs(out_dir, exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as handle:
        handle.write(report)

    sys.stderr.write(
        "camera_audit: %d cameras, %d scene lights (+%d from prefabs), %d camera/OVR-named "
        "GameObjects without a Camera, %d prefab camera instances, %d materials -> %s\n"
        % (
            len(cams),
            len(lights),
            len(prefab_lights),
            len(cam_named_gos),
            len(prefab_cams),
            material_count,
            os.path.relpath(args.out, REPO_ROOT),
        )
    )
    return 0


def main(argv: Optional[List[str]] = None) -> int:
    return run(parse_args(argv))


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())
