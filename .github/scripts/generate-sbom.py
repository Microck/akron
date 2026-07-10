#!/usr/bin/env python3
"""Generate a deterministic CycloneDX SBOM for an Akron player archive."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
from pathlib import Path
import urllib.parse
import uuid
import zipfile


REPOSITORY_URL = "https://github.com/Microck/akron"
DEEPCLONER_COMMIT = "770af80c096df3ebc9db57f567f2924e111a9ec9"


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def nuget_purl(name: str, version: str) -> str:
    encoded_name = urllib.parse.quote(name, safe=".")
    encoded_version = urllib.parse.quote(version, safe=".")
    return f"pkg:nuget/{encoded_name}@{encoded_version}"


def load_nuget_components(
    lock_paths: list[Path], runtime_package_names: set[str]
) -> list[dict[str, object]]:
    packages: dict[tuple[str, str], dict[str, object]] = {}

    for lock_path in lock_paths:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
        frameworks = lock.get("dependencies")
        if not isinstance(frameworks, dict):
            raise ValueError(f"NuGet lock has no dependency graph: {lock_path}")

        for dependencies in frameworks.values():
            if not isinstance(dependencies, dict):
                continue
            for name, dependency in dependencies.items():
                if not isinstance(dependency, dict):
                    continue
                if name not in runtime_package_names:
                    continue
                resolved = dependency.get("resolved")
                content_hash = dependency.get("contentHash")
                dependency_type = dependency.get("type")
                if dependency_type == "Project":
                    continue
                if not isinstance(resolved, str) or not isinstance(content_hash, str):
                    raise ValueError(f"NuGet dependency is not locked with a hash: {name}")

                key = (name, resolved)
                package = packages.setdefault(
                    key,
                    {
                        "type": "library",
                        "bom-ref": nuget_purl(name, resolved),
                        "name": name,
                        "version": resolved,
                        "purl": nuget_purl(name, resolved),
                        "hashes": [
                            {
                                "alg": "SHA-512",
                                "content": base64.b64decode(content_hash, validate=True).hex(),
                            }
                        ],
                        "properties": [],
                    },
                )
                properties = package["properties"]
                if not isinstance(properties, list):
                    raise ValueError(f"Invalid component properties for {name}")
                properties.extend(
                    [
                        {"name": "akron:nuget:lockfile", "value": lock_path.as_posix()},
                        {"name": "akron:nuget:type", "value": str(dependency_type)},
                    ]
                )

    missing_packages = runtime_package_names - {name for name, _ in packages}
    if missing_packages:
        raise ValueError(
            "Runtime packages are missing from the supplied lockfiles: "
            + ", ".join(sorted(missing_packages))
        )

    components = []
    for package in packages.values():
        properties = package["properties"]
        if isinstance(properties, list):
            unique_properties = {
                (str(item["name"]), str(item["value"]))
                for item in properties
                if isinstance(item, dict) and "name" in item and "value" in item
            }
            package["properties"] = [
                {"name": name, "value": value}
                for name, value in sorted(unique_properties)
            ]
        components.append(package)

    return sorted(components, key=lambda component: str(component["bom-ref"]))


def load_archive_components(archive_path: Path) -> list[dict[str, object]]:
    components = []
    with zipfile.ZipFile(archive_path) as archive:
        for entry in sorted(archive.infolist(), key=lambda item: item.filename):
            if entry.is_dir():
                continue
            content = archive.read(entry)
            bom_ref = f"file:{urllib.parse.quote(entry.filename, safe='/.-_')}"
            components.append(
                {
                    "type": "file",
                    "bom-ref": bom_ref,
                    "name": entry.filename,
                    "hashes": [{"alg": "SHA-256", "content": sha256_bytes(content)}],
                    "properties": [
                        {"name": "akron:archive:path", "value": entry.filename},
                        {"name": "akron:archive:size", "value": str(entry.file_size)},
                    ],
                }
            )
    return components


def load_runtime_package_names(archive_path: Path) -> set[str]:
    with zipfile.ZipFile(archive_path) as archive:
        try:
            deps = json.loads(archive.read("bin/Akron.deps.json"))
        except KeyError as exception:
            raise ValueError("Player archive is missing bin/Akron.deps.json") from exception

    targets = deps.get("targets")
    libraries = deps.get("libraries")
    if not isinstance(targets, dict) or len(targets) != 1 or not isinstance(libraries, dict):
        raise ValueError("Akron.deps.json has an unsupported dependency graph")

    target = next(iter(targets.values()))
    if not isinstance(target, dict):
        raise ValueError("Akron.deps.json target graph is invalid")

    package_names: set[str] = set()
    for library_ref, assets in target.items():
        library = libraries.get(library_ref)
        if not isinstance(library, dict) or library.get("type") != "package":
            continue
        if not isinstance(assets, dict):
            raise ValueError(f"Runtime asset graph is invalid for {library_ref}")
        runtime = assets.get("runtime")
        runtime_targets = assets.get("runtimeTargets")
        has_runtime_assets = (isinstance(runtime, dict) and bool(runtime)) or (
            isinstance(runtime_targets, dict) and bool(runtime_targets)
        )
        if has_runtime_assets:
            name, separator, version = library_ref.rpartition("/")
            if not separator or not name or not version:
                raise ValueError(f"Runtime package reference is invalid: {library_ref}")
            package_names.add(name)

    if not package_names:
        raise ValueError("Akron.deps.json identifies no shipped runtime packages")
    return package_names


def deepcloner_component() -> dict[str, object]:
    return {
        "type": "library",
        "bom-ref": f"pkg:github/force-net/DeepCloner@{DEEPCLONER_COMMIT}",
        "name": "Force.DeepCloner",
        "version": "0.10.2+akron",
        "purl": f"pkg:github/force-net/DeepCloner@{DEEPCLONER_COMMIT}",
        "licenses": [{"license": {"id": "MIT"}}],
        "externalReferences": [{"type": "vcs", "url": "https://github.com/force-net/DeepCloner"}],
        "properties": [
            {"name": "akron:vendored", "value": "true"},
            {"name": "akron:provenance", "value": "Source/vendor/deepcloner/provenance.md"},
        ],
    }


def generate_bom(
    archive_path: Path,
    version: str,
    lock_paths: list[Path],
    runtime_package_names: set[str],
) -> dict[str, object]:
    archive_sha256 = sha256_file(archive_path)
    application_ref = f"pkg:github/Microck/akron@v{version}"
    components = load_nuget_components(lock_paths, runtime_package_names)
    components.append(deepcloner_component())
    components.extend(load_archive_components(archive_path))
    components.sort(key=lambda component: str(component["bom-ref"]))

    serial = uuid.uuid5(
        uuid.NAMESPACE_URL,
        f"{REPOSITORY_URL}/releases/tag/v{version}#{archive_sha256}",
    )
    return {
        "bomFormat": "CycloneDX",
        "specVersion": "1.6",
        "serialNumber": f"urn:uuid:{serial}",
        "version": 1,
        "metadata": {
            "component": {
                "type": "application",
                "bom-ref": application_ref,
                "name": "Akron",
                "version": version,
                "purl": application_ref,
                "hashes": [{"alg": "SHA-256", "content": archive_sha256}],
                "externalReferences": [{"type": "vcs", "url": REPOSITORY_URL}],
            },
            "properties": [
                {"name": "akron:generator", "value": ".github/scripts/generate-sbom.py"},
                {"name": "akron:generator:version", "value": "1"},
            ],
        },
        "components": components,
        "dependencies": [
            {
                "ref": application_ref,
                "dependsOn": sorted(str(component["bom-ref"]) for component in components),
            }
        ],
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--lock",
        action="append",
        type=Path,
        default=[],
        help="NuGet packages.lock.json path; may be repeated",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    if not args.archive.is_file():
        raise ValueError(f"Archive does not exist: {args.archive}")
    if not args.version or args.version.startswith("v"):
        raise ValueError("--version must be a non-empty version without a leading v")
    if not args.lock:
        raise ValueError("At least one --lock path is required")
    for lock_path in args.lock:
        if not lock_path.is_file():
            raise ValueError(f"NuGet lock does not exist: {lock_path}")

    runtime_package_names = load_runtime_package_names(args.archive)
    bom = generate_bom(args.archive, args.version, args.lock, runtime_package_names)
    args.output.write_text(
        json.dumps(bom, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
