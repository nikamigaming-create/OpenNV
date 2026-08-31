from __future__ import annotations

from pathlib import Path


def read_csharp_source_module(entrypoint: Path) -> str:
    """Read a C# type's entrypoint and responsibility-focused partial files."""
    paths = sorted(entrypoint.parent.glob(f"{entrypoint.stem}*.cs"))
    if entrypoint not in paths:
        raise FileNotFoundError(entrypoint)
    return "\n".join(path.read_text(encoding="utf-8") for path in paths)
