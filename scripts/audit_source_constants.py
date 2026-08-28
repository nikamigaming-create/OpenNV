#!/usr/bin/env python3
"""Reject unexplained literals and unscanned languages in OpenNV production code."""

from __future__ import annotations

import ast
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path


MATHEMATICAL_LITERALS = frozenset({-4, -3, -2, -1, 0, 1, 2, 3, 4})
CS_NUMBER = re.compile(
    r"(?<![A-Za-z_0-9])[-+]?(?:\d+(?:_\d+)*(?:\.\d+(?:_\d+)*)?|\.\d+)"
    r"(?:[eE][-+]?\d+)?(?:[fFdDmMuUlL]+)?(?![A-Za-z_0-9])"
)
FORM_ID_STRING = re.compile(
    r"(?i)(?P<quote>[\"'])(?P<value>(?:0x)?[0-9a-f]{8})(?P=quote)"
)
SHA256_STRING = re.compile(
    r"(?i)(?P<quote>[\"'])(?P<value>[0-9a-f]{64})(?P=quote)"
)
OWNED_ASSET_PATH_STRING = re.compile(
    r"(?i)(?P<quote>[\"'])(?P<value>(?:meshes|textures|sound|music|video|menus)"
    r"[\\/]+[^\"'{}\r\n]+\.(?:nif|dds|kf|egm|egt|spt))(?P=quote)"
)
FORBIDDEN_SUBSTITUTION_WORD = re.compile(r"(?i)fallback|heuristic|\bguess(?:ed|es|ing)?\b")
PERMITTED_SUBSTITUTION_IDENTIFIERS = frozenset({"AllowSystemFallback"})
GODOT_DUPLICATED_POLICY_KEYS = frozenset(
    {
        "environment/defaults/default_clear_color",
        "window/size/viewport_height",
        "window/size/viewport_width",
    }
)
DECLARATIVE_CONFIGURATION_GLOBS = (
    ".github/workflows/*.yml",
    "content/recipes/*.json",
    "desktop/package.json",
    "desktop/src/*.json",
    "desktop/src/renderer/index.html",
    "desktop/src/renderer/styles.css",
    "release/*.json",
    "runtime/config/*.json",
    "runtime/*.cfg",
    "runtime/*.godot",
    "runtime/*.json",
    "runtime/*.tres",
    "runtime/*.tscn",
)


@dataclass(frozen=True)
class Violation:
    path: Path
    line: int
    value: str
    language: str


def source_data_violations(
    path: Path,
    forbidden_identities: frozenset[str] = frozenset(),
) -> list[Violation]:
    source = path.read_text(encoding="utf-8-sig")
    violations: list[Violation] = []

    def add(match: re.Match[str], value: str, kind: str) -> None:
        violations.append(
            Violation(
                path,
                source.count("\n", 0, match.start()) + 1,
                value,
                kind,
            )
        )

    for match in FORM_ID_STRING.finditer(source):
        value = match.group("value")
        if value.casefold().removeprefix("0x") != "00000000":
            add(match, value, "content-form-id")
    for match in SHA256_STRING.finditer(source):
        add(match, match.group("value"), "content-sha256")
    for match in OWNED_ASSET_PATH_STRING.finditer(source):
        add(match, match.group("value"), "owned-asset-path")
    for identity in sorted(forbidden_identities, key=lambda value: (-len(value), value)):
        pattern = (
            r"(?<![A-Za-z0-9_])" + re.escape(identity) + r"(?![A-Za-z0-9_])"
        )
        for match in re.finditer(pattern, source, flags=re.IGNORECASE):
            add(match, identity, "content-identity")
    for match in FORBIDDEN_SUBSTITUTION_WORD.finditer(source):
        line_start = source.rfind("\n", 0, match.start()) + 1
        line_end = source.find("\n", match.end())
        if line_end < 0:
            line_end = len(source)
        if any(
            identifier in source[line_start:line_end]
            for identifier in PERMITTED_SUBSTITUTION_IDENTIFIERS
        ):
            continue
        add(match, match.group(0), "guessed-substitution")
    unique: dict[tuple[int, str, str], Violation] = {}
    for violation in violations:
        unique[(violation.line, violation.value.casefold(), violation.language)] = violation
    return list(unique.values())


def python_violations(path: Path) -> list[Violation]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    parents = {
        child: parent
        for parent in ast.walk(tree)
        for child in ast.iter_child_nodes(parent)
    }
    violations: list[Violation] = []
    for node in ast.walk(tree):
        if (
            not isinstance(node, ast.Constant)
            or isinstance(node.value, bool)
            or not isinstance(node.value, (int, float))
            or node.value in MATHEMATICAL_LITERALS
        ):
            continue
        if assigned_to_module_contract(node, parents):
            continue
        violations.append(Violation(path, node.lineno, repr(node.value), "python"))
    return violations


def assigned_to_module_contract(node: ast.AST, parents: dict[ast.AST, ast.AST]) -> bool:
    current = node
    while current in parents:
        current = parents[current]
        if isinstance(current, (ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda, ast.ClassDef)):
            return False
        if isinstance(current, (ast.Assign, ast.AnnAssign)):
            targets = current.targets if isinstance(current, ast.Assign) else [current.target]
            names = [target.id for target in targets if isinstance(target, ast.Name)]
            return bool(names) and all(name.isupper() for name in names)
    return False


def csharp_violations(path: Path) -> list[Violation]:
    source = path.read_text(encoding="utf-8")
    stripped = strip_csharp_noncode(source)
    lines = source.splitlines()
    violations: list[Violation] = []
    for match in CS_NUMBER.finditer(stripped):
        token = match.group(0)
        numeric = token.rstrip("fFdDmMuUlL").replace("_", "")
        try:
            value = float(numeric)
        except ValueError:
            continue
        if value in MATHEMATICAL_LITERALS:
            continue
        line = stripped.count("\n", 0, match.start()) + 1
        if " const " in f" {lines[line - 1]} ":
            continue
        violations.append(Violation(path, line, token, "csharp"))
    return violations


def javascript_violations(path: Path) -> list[Violation]:
    source = path.read_text(encoding="utf-8")
    stripped = strip_javascript_noncode(source)
    lines = source.splitlines()
    violations: list[Violation] = []
    for match in CS_NUMBER.finditer(stripped):
        token = match.group(0)
        value = float(token.replace("_", ""))
        if value in MATHEMATICAL_LITERALS:
            continue
        line = stripped.count("\n", 0, match.start()) + 1
        if re.match(r"^\s*const\s+[A-Z][A-Z0-9_]*\s*=", lines[line - 1]):
            continue
        violations.append(Violation(path, line, token, "javascript"))
    return violations


def powershell_violations(path: Path) -> list[Violation]:
    source = path.read_text(encoding="utf-8-sig")
    violations: list[Violation] = []
    for line_number, source_line in enumerate(source.splitlines(), start=1):
        line = strip_powershell_noncode(source_line)
        for match in CS_NUMBER.finditer(line):
            token = match.group(0)
            value = float(token.replace("_", ""))
            if value in MATHEMATICAL_LITERALS:
                continue
            if re.match(r"^\s*\$[A-Z][A-Za-z0-9]*\s*=", source_line):
                continue
            violations.append(Violation(path, line_number, token, "powershell"))
    return violations


def strip_csharp_noncode(source: str) -> str:
    output = list(source)
    index = 0
    state = "code"
    quote_count = 0
    while index < len(source):
        pair = source[index : index + 2]
        if state == "code":
            if pair == "//":
                state = "line-comment"
                output[index] = output[index + 1] = " "
                index += 2
                continue
            if pair == "/*":
                state = "block-comment"
                output[index] = output[index + 1] = " "
                index += 2
                continue
            if source[index] == '"':
                quote_count = 1
                while index + quote_count < len(source) and source[index + quote_count] == '"':
                    quote_count += 1
                state = "raw-string" if quote_count >= 3 else "string"
                output[index] = " "
            elif source[index] == "'":
                state = "character"
                output[index] = " "
        elif state == "line-comment":
            if source[index] == "\n":
                state = "code"
            else:
                output[index] = " "
        elif state == "block-comment":
            output[index] = " " if source[index] != "\n" else "\n"
            if pair == "*/":
                output[index + 1] = " "
                state = "code"
                index += 2
                continue
        elif state == "string":
            output[index] = " " if source[index] != "\n" else "\n"
            if source[index] == "\\":
                if index + 1 < len(source):
                    output[index + 1] = " "
                    index += 2
                    continue
            elif source[index] == '"':
                state = "code"
        elif state == "character":
            output[index] = " " if source[index] != "\n" else "\n"
            if source[index] == "\\":
                if index + 1 < len(source):
                    output[index + 1] = " "
                    index += 2
                    continue
            elif source[index] == "'":
                state = "code"
        elif state == "raw-string":
            output[index] = " " if source[index] != "\n" else "\n"
            if source.startswith('"' * quote_count, index):
                for offset in range(quote_count):
                    output[index + offset] = " "
                index += quote_count
                state = "code"
                continue
        index += 1
    return "".join(output)


def strip_javascript_noncode(source: str) -> str:
    output = list(strip_csharp_noncode(source))
    in_template = False
    escaped = False
    for index, character in enumerate(source):
        if character == "`" and not escaped:
            in_template = not in_template
            output[index] = " "
        elif in_template and character != "\n":
            output[index] = " "
        escaped = in_template and character == "\\" and not escaped
        if character != "\\":
            escaped = False
    return "".join(output)


def strip_powershell_noncode(source: str) -> str:
    output = list(source)
    quote: str | None = None
    index = 0
    while index < len(source):
        character = source[index]
        if quote is None:
            if character in ("'", '"'):
                quote = character
                output[index] = " "
            elif character == "#":
                for remainder in range(index, len(source)):
                    output[remainder] = " "
                break
        else:
            output[index] = " "
            if quote == '"' and character == "`":
                if index + 1 < len(source):
                    output[index + 1] = " "
                    index += 1
            elif character == quote:
                if quote == "'" and index + 1 < len(source) and source[index + 1] == "'":
                    output[index + 1] = " "
                    index += 1
                else:
                    quote = None
        index += 1
    return "".join(output)


def godot_project_violations(path: Path) -> list[Violation]:
    violations: list[Violation] = []
    for line_number, source_line in enumerate(
        path.read_text(encoding="utf-8-sig").splitlines(),
        start=1,
    ):
        key = source_line.partition("=")[0].strip()
        if key in GODOT_DUPLICATED_POLICY_KEYS:
            violations.append(Violation(path, line_number, key, "godot-project-policy"))
    return violations


def production_sources(repository: Path) -> list[tuple[Path, str]]:
    sources: list[tuple[Path, str]] = []
    sources.extend(
        (path, "python")
        for base in (repository / "content" / "tools", repository / "content" / "hooks")
        for path in sorted(base.glob("*.py"))
    )
    sources.append((repository / "content" / "OpenNV.Content.spec", "python"))
    sources.append((repository / "scripts" / "audit_source_constants.py", "python"))
    sources.extend(
        (path, "csharp")
        for path in sorted((repository / "runtime" / "src").rglob("*.cs"))
    )
    sources.extend(
        (path, "javascript")
        for path in sorted((repository / "desktop" / "src").rglob("*"))
        if path.is_file() and path.suffix.casefold() in {".cjs", ".mjs"}
    )
    sources.extend(
        (path, "powershell")
        for path in sorted((repository / "scripts").glob("*.ps1"))
    )
    return sources


def unsupported_source_violations(repository: Path) -> list[Violation]:
    roots = {
        repository / "content" / "hooks": {".py"},
        repository / "content" / "tools": {".py"},
        repository / "desktop" / "src": {".cjs", ".css", ".html", ".json", ".mjs"},
        repository / "runtime" / "src": {".cs", ".uid"},
        repository / "scripts": {".ps1", ".py"},
    }
    violations: list[Violation] = []
    for root, supported_suffixes in roots.items():
        for path in sorted(candidate for candidate in root.rglob("*") if candidate.is_file()):
            if "__pycache__" in path.parts:
                continue
            if path.suffix.casefold() not in supported_suffixes:
                violations.append(
                    Violation(path, 1, path.suffix or "<none>", "unsupported-source")
                )
    return violations


def configuration_surfaces(repository: Path) -> list[Path]:
    return sorted(
        {
            path
            for pattern in DECLARATIVE_CONFIGURATION_GLOBS
            for path in repository.glob(pattern)
            if path.is_file()
        }
    )


def content_identities(repository: Path) -> frozenset[str]:
    identities: set[str] = set()
    for path in sorted((repository / "content" / "recipes").glob("*.json")):
        document = json.loads(path.read_text(encoding="utf-8"))
        recipe_id = document.get("id")
        if isinstance(recipe_id, str) and recipe_id:
            identities.add(recipe_id)
        if isinstance(document.get("subjects"), list):
            for subject in document["subjects"]:
                if not isinstance(subject, dict):
                    continue
                for field in ("id", "label"):
                    value = subject.get(field)
                    if isinstance(value, str) and value:
                        identities.add(value)
        if isinstance(document.get("locations"), list):
            for location in document["locations"]:
                if not isinstance(location, dict):
                    continue
                for field in ("id", "location"):
                    value = location.get(field)
                    if isinstance(value, str) and value:
                        identities.add(value)
    runtime = json.loads(
        (repository / "runtime" / "config" / "open-nv-runtime-v1.json").read_text(
            encoding="utf-8"
        )
    )
    owned_data = runtime["legalAssets"]["ownedData"]
    identities.add(str(owned_data["masterFile"]))
    identities.add(str(owned_data["meshesArchiveFile"]))
    identities.update(str(value) for value in owned_data["textureArchiveFiles"])
    return frozenset(identities)


def configuration_substitution_violations(repository: Path) -> list[Violation]:
    violations: list[Violation] = []
    for path in configuration_surfaces(repository):
        if path.suffix.casefold() != ".json":
            continue
        source = path.read_text(encoding="utf-8-sig")
        try:
            json.loads(source)
        except json.JSONDecodeError:
            continue
        for match in FORBIDDEN_SUBSTITUTION_WORD.finditer(source):
            violations.append(
                Violation(
                    path,
                    source.count("\n", 0, match.start()) + 1,
                    match.group(0),
                    "configuration-guessed-substitution",
                )
            )
    return violations


def main() -> int:
    repository = Path(__file__).resolve().parents[1]
    violations: list[Violation] = []
    scanners = {
        "csharp": csharp_violations,
        "javascript": javascript_violations,
        "powershell": powershell_violations,
        "python": python_violations,
    }
    sources = production_sources(repository)
    for path, language in sources:
        violations.extend(scanners[language](path))
    violations.extend(unsupported_source_violations(repository))
    violations.extend(godot_project_violations(repository / "runtime" / "project.godot"))
    identities = content_identities(repository)
    for path, _language in sources:
        if path.resolve() == Path(__file__).resolve():
            continue
        violations.extend(source_data_violations(path, identities))
    violations.extend(configuration_substitution_violations(repository))
    if violations:
        for violation in violations:
            relative = violation.path.relative_to(repository)
            print(
                f"{relative}:{violation.line}: unexplained {violation.language} literal "
                f"{violation.value}"
            )
        print(f"OPENNV_SOURCE_CONSTANT_POLICY_FAIL violations={len(violations)}")
        return 1
    source_lines = sum(
        len(path.read_text(encoding="utf-8-sig").splitlines())
        for path, _language in sources
    )
    print(
        "OPENNV_SOURCE_CONSTANT_POLICY_PASS "
        f"violations=0 sourceFiles={len(sources)} sourceLines={source_lines} "
        f"configurationFiles={len(configuration_surfaces(repository))}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
