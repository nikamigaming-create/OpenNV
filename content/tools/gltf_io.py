"""Small deterministic glTF buffer and atomic-output helpers."""

from __future__ import annotations

import ast
import hashlib
import os
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable


SOURCE_LENGTH_BYTES = 8
GL_FLOAT = 5126
GL_UNSIGNED_SHORT = 5123
GL_UNSIGNED_INT = 5125
GL_ARRAY_BUFFER = 34962
GL_ELEMENT_ARRAY_BUFFER = 34963
GL_UNSIGNED_SHORT_MAX = 65535
GL_LINEAR = 9729
GL_LINEAR_MIPMAP_LINEAR = 9987
GL_REPEAT = 10497


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def compiler_sources_sha256(paths: Iterable[Path]) -> str:
    rows = []
    for path in sorted((value.resolve() for value in paths), key=lambda value: value.name):
        payload = path.read_bytes()
        rows.append(
            path.name.encode()
            + b"\0"
            + len(payload).to_bytes(SOURCE_LENGTH_BYTES, "little")
            + payload
        )
    return sha256_bytes(b"".join(rows))


def local_python_dependency_paths(
    entrypoint: Path,
    module_root: Path,
    *,
    excluded_modules: Iterable[str] = (),
) -> list[Path]:
    """Return the fail-closed transitive local-import graph for one entrypoint."""
    root = module_root.resolve()
    pending = [entrypoint.resolve()]
    excluded = set(excluded_modules)
    discovered: set[Path] = set()
    while pending:
        source = pending.pop()
        if source in discovered:
            continue
        if source.suffix != ".py" or not source.is_file() or not source.is_relative_to(root):
            raise ValueError(f"Compiler dependency is outside the local module root: {source}")
        discovered.add(source)
        tree = ast.parse(source.read_text(encoding="utf-8"), filename=str(source))
        for node in ast.walk(tree):
            if isinstance(node, ast.Call) and (
                isinstance(node.func, ast.Name) and node.func.id == "__import__"
                or isinstance(node.func, ast.Attribute)
                and isinstance(node.func.value, ast.Name)
                and node.func.value.id == "importlib"
                and node.func.attr == "import_module"
            ):
                raise ValueError(
                    f"Dynamic import cannot establish compiler provenance: {source}:{node.lineno}"
                )
            imported_names: list[str] = []
            if isinstance(node, ast.Import):
                imported_names.extend(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom):
                if node.level:
                    raise ValueError(
                        f"Relative import cannot establish compiler provenance: {source}:{node.lineno}"
                    )
                if node.module:
                    imported_names.append(node.module)
            for imported_name in imported_names:
                top_level = imported_name.split(".", 1)[0]
                if top_level in excluded:
                    continue
                module_path = root.joinpath(*imported_name.split(".")).with_suffix(".py")
                package_path = root.joinpath(*imported_name.split("."), "__init__.py")
                candidates = [path for path in (module_path, package_path) if path.is_file()]
                if len(candidates) > 1:
                    raise ValueError(f"Ambiguous local compiler import: {imported_name}")
                pending.extend(candidates)
    return sorted(discovered, key=lambda path: path.relative_to(root).as_posix())


@dataclass
class BufferBuilder:
    data: bytearray = field(default_factory=bytearray)
    views: list[dict[str, object]] = field(default_factory=list)
    accessors: list[dict[str, object]] = field(default_factory=list)

    def add(
        self,
        payload: bytes,
        *,
        component_type: int,
        count: int,
        value_type: str,
        target: int | None,
        minimum: list[float] | None = None,
        maximum: list[float] | None = None,
    ) -> int:
        while len(self.data) % 4:
            self.data.append(0)
        offset = len(self.data)
        self.data.extend(payload)
        view_index = len(self.views)
        view: dict[str, object] = {"buffer": 0, "byteOffset": offset, "byteLength": len(payload)}
        if target is not None:
            view["target"] = target
        self.views.append(view)
        accessor: dict[str, object] = {
            "bufferView": view_index,
            "componentType": component_type,
            "count": count,
            "type": value_type,
        }
        if minimum is not None:
            accessor["min"] = minimum
        if maximum is not None:
            accessor["max"] = maximum
        self.accessors.append(accessor)
        return len(self.accessors) - 1


def pack_floats(rows: Iterable[Iterable[float]]) -> bytes:
    flat = [float(value) for row in rows for value in row]
    return struct.pack(f"<{len(flat)}f", *flat)


def atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)
