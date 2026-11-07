"""Minimal, dependency-free reader for Unity text-serialized assets.

Unity's ``.unity`` / ``.prefab`` / ``.mat`` files are *almost* YAML: they use
custom ``!u!<classId>`` tags and repeated document headers that make strict
loaders (PyYAML included) choke or silently drop data.  Everything in this
module is stdlib only and line based so the audit stays reproducible on a bare
Python install.

Nothing here writes to disk; all files are opened read-only.
"""

from __future__ import annotations

import os
import re
from typing import Any, Dict, Iterable, List, Optional, Tuple

DOC_HEADER = re.compile(r"^---\s+!u!(\d+)\s+&(-?\d+)(\s+stripped)?\s*$")
INLINE_MAP_KV = re.compile(r"([A-Za-z0-9_\-.]+)\s*:\s*([^,}]+)")
KEY_LINE = re.compile(r"^(\s*)(-\s+)?([^:]+?):\s*(.*)$")
LIST_ITEM = re.compile(r"^(\s*)-\s*(.*)$")


class Document:
    """One ``--- !u!<class> &<fileID>`` block from a Unity text asset."""

    __slots__ = ("class_id", "file_id", "stripped", "type_name", "body", "line")

    def __init__(self, class_id: int, file_id: int, stripped: bool, line: int):
        self.class_id = class_id
        self.file_id = file_id
        self.stripped = stripped
        self.line = line
        self.type_name = ""
        self.body: Dict[str, Any] = {}

    def __repr__(self) -> str:  # pragma: no cover - debug helper
        return "<Document !u!%d &%d %s>" % (
            self.class_id,
            self.file_id,
            self.type_name,
        )

    def get(self, path: str, default: Any = None) -> Any:
        return get_path(self.body, path, default)


def _coerce_scalar(text: str) -> Any:
    text = text.strip()
    if text == "":
        return ""
    if text.startswith("'") and text.endswith("'") and len(text) >= 2:
        return text[1:-1]
    if text.startswith('"') and text.endswith('"') and len(text) >= 2:
        return text[1:-1]
    if text.startswith("{") or text.startswith("["):
        return _parse_inline(text)
    try:
        return int(text)
    except ValueError:
        pass
    try:
        return float(text)
    except ValueError:
        pass
    return text


def _parse_inline(text: str) -> Any:
    text = text.strip()
    if text in ("{}", "[]"):
        return {} if text == "{}" else []
    if text.startswith("{") and text.endswith("}"):
        out: Dict[str, Any] = {}
        for key, raw in INLINE_MAP_KV.findall(text[1:-1]):
            out[key] = _coerce_scalar(raw)
        return out
    if text.startswith("[") and text.endswith("]"):
        inner = text[1:-1].strip()
        if not inner:
            return []
        return [_coerce_scalar(part) for part in inner.split(",")]
    return text


def _join_continuations(lines: Iterable[str]) -> List[str]:
    """Rejoin Unity's wrapped inline maps (``{fileID: 1, guid: x,\\n  type: 3}``)."""
    joined: List[str] = []
    pending: Optional[str] = None
    for raw in lines:
        line = raw.rstrip("\n").rstrip("\r")
        if pending is not None:
            pending = pending + " " + line.strip()
            if pending.count("{") <= pending.count("}"):
                joined.append(pending)
                pending = None
            continue
        if line.count("{") > line.count("}"):
            pending = line
            continue
        joined.append(line)
    if pending is not None:
        joined.append(pending)
    return joined


def _indent_of(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def _next_content(lines: List[str], start: int) -> int:
    i = start
    while i < len(lines) and not lines[i].strip():
        i += 1
    return i


def _parse_sequence(lines: List[str], start: int, indent: int) -> Tuple[List[Any], int]:
    """Consume ``- ...`` items at exactly ``indent``; returns (list, next_index)."""
    out: List[Any] = []
    i = start
    while i < len(lines):
        if not lines[i].strip():
            i += 1
            continue
        cur = _indent_of(lines[i])
        if cur != indent:
            break
        item = LIST_ITEM.match(lines[i])
        if item is None:
            break
        rest = item.group(2).strip()
        if rest.startswith("{") or rest.startswith("["):
            out.append(_parse_inline(rest))
            i += 1
            continue
        if rest == "":
            # "-" alone: nested block underneath.
            j = _next_content(lines, i + 1)
            if j < len(lines) and _indent_of(lines[j]) > indent:
                value, j2 = _parse_block(lines, j, _indent_of(lines[j]))
                out.append(value)
                i = j2
                continue
            out.append("")
            i += 1
            continue
        if KEY_LINE.match("  " + rest) and ":" in rest:
            # "- key: value" plus any deeper sibling keys belonging to the item.
            item_indent = indent + 2
            sub = [" " * item_indent + rest]
            j = i + 1
            while j < len(lines):
                if not lines[j].strip():
                    j += 1
                    continue
                if _indent_of(lines[j]) <= indent:
                    break
                sub.append(lines[j])
                j += 1
            value, _ = _parse_block(sub, 0, item_indent)
            out.append(value)
            i = j
            continue
        out.append(_coerce_scalar(rest))
        i += 1
    return out, i


def _parse_mapping(lines: List[str], start: int, indent: int) -> Tuple[Dict[str, Any], int]:
    out: Dict[str, Any] = {}
    i = start
    while i < len(lines):
        if not lines[i].strip():
            i += 1
            continue
        cur = _indent_of(lines[i])
        if cur < indent:
            break
        if cur > indent or LIST_ITEM.match(lines[i]):
            break
        kv = KEY_LINE.match(lines[i])
        if kv is None:
            break
        key = kv.group(3).strip()
        rest = kv.group(4).strip()
        if rest != "":
            out[key] = _coerce_scalar(rest)
            i += 1
            continue
        # Empty value: nested mapping (deeper indent), a sequence at the same or
        # deeper indent (both legal YAML, Unity emits both), or an empty scalar.
        j = _next_content(lines, i + 1)
        if j >= len(lines):
            out[key] = ""
            i = j
            continue
        nxt_indent = _indent_of(lines[j])
        if LIST_ITEM.match(lines[j]) and nxt_indent >= indent:
            value, j2 = _parse_sequence(lines, j, nxt_indent)
            out[key] = value
            i = j2
            continue
        if nxt_indent > indent:
            value, j2 = _parse_block(lines, j, nxt_indent)
            out[key] = value
            i = j2
            continue
        out[key] = ""
        i += 1
    return out, i


def _parse_block(lines: List[str], start: int, indent: int) -> Tuple[Any, int]:
    """Parse an indentation block; returns (value, next_index)."""
    i = _next_content(lines, start)
    if i >= len(lines):
        return {}, i
    if LIST_ITEM.match(lines[i]):
        return _parse_sequence(lines, i, _indent_of(lines[i]))
    return _parse_mapping(lines, i, indent)


def parse_documents(path: str) -> List[Document]:
    """Split a Unity text asset into :class:`Document` objects."""
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        raw_lines = handle.readlines()

    docs: List[Document] = []
    current: Optional[Document] = None
    buffer: List[str] = []

    def flush() -> None:
        if current is None:
            return
        lines = _join_continuations(buffer)
        # First non-empty line is the type name, e.g. "Camera:".
        idx = 0
        while idx < len(lines) and not lines[idx].strip():
            idx += 1
        if idx < len(lines) and lines[idx].rstrip().endswith(":") and _indent_of(lines[idx]) == 0:
            current.type_name = lines[idx].strip().rstrip(":")
            idx += 1
        body_lines = lines[idx:]
        if body_lines:
            indents = [_indent_of(l) for l in body_lines if l.strip()]
            base = min(indents) if indents else 0
            value, _ = _parse_block(body_lines, 0, base)
            if isinstance(value, dict):
                current.body = value
            else:
                current.body = {"__seq__": value}
        docs.append(current)

    for number, raw in enumerate(raw_lines, start=1):
        header = DOC_HEADER.match(raw.rstrip("\n"))
        if header:
            flush()
            current = Document(
                class_id=int(header.group(1)),
                file_id=int(header.group(2)),
                stripped=bool(header.group(3)),
                line=number,
            )
            buffer = []
            continue
        if current is not None:
            buffer.append(raw)
    flush()
    return docs


def get_path(node: Any, path: str, default: Any = None) -> Any:
    cur = node
    for part in path.split("."):
        if isinstance(cur, dict) and part in cur:
            cur = cur[part]
        else:
            return default
    return cur


def read_guid(meta_path: str) -> Optional[str]:
    """Return the ``guid:`` from a ``.meta`` file, or None."""
    if not os.path.isfile(meta_path):
        return None
    with open(meta_path, "r", encoding="utf-8", errors="replace") as handle:
        for line in handle:
            if line.startswith("guid:"):
                return line.split(":", 1)[1].strip()
    return None


def build_guid_index(roots: Iterable[str], suffixes: Iterable[str]) -> Dict[str, str]:
    """Map guid -> asset path for every ``*.meta`` under ``roots``."""
    wanted = tuple(suffixes)
    index: Dict[str, str] = {}
    for root in roots:
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in ("Library", "Temp", "obj")]
            for name in filenames:
                if not name.endswith(".meta"):
                    continue
                asset = name[: -len(".meta")]
                if wanted and not asset.endswith(wanted):
                    continue
                meta_path = os.path.join(dirpath, name)
                guid = read_guid(meta_path)
                if guid:
                    index[guid] = os.path.join(dirpath, asset)
    return index
