"""Compile supported owned Gamebryo particle graphs into a runtime contract."""

from __future__ import annotations

import json
import time
from pathlib import Path

if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from pyffi.formats.nif import NifFormat  # type: ignore

from compiler_provenance import compiler_provenance
from gltf_io import atomic_write, sha256_bytes
from nif_decoder import decode_nif


PARTICLE_SCHEMA = "opennv-owned-nif-particle-effect/v1"
STATIC_SCHEMA = "opennv-static-nif-gltf/v3"
GENERATOR = "OpenNV owned NIF particle exporter v1"
_ENGINE_MODIFIERS = (NifFormat.NiPSysAgeDeathModifier, NifFormat.NiPSysPositionModifier,
                     NifFormat.NiPSysBoundUpdateModifier)


class UnsupportedParticleEffectError(ValueError):
    pass


def _text(value: object) -> str:
    return bytes(value).decode("utf-8", errors="strict") if not isinstance(value, str) else value


def _vector(value: object) -> list[float]:
    return [float(value.x), float(value.z), -float(value.y)]


def _color(value: object) -> list[float]:
    return [float(value.r), float(value.g), float(value.b), float(value.a)]


def _range(value: float, variation: float) -> list[float]:
    return [float(value - variation), float(value + variation)]


def _keys(interpolator: object, *, boolean: bool = False) -> list[dict[str, object]]:
    data = getattr(interpolator, "data", None)
    if data is None:
        field = "bool_value" if boolean else "float_value"
        return [{"timeSeconds": 0.0, "value": bool(getattr(interpolator, field)) if boolean
                 else float(getattr(interpolator, field))}]
    group = data.data
    interpolation = str(group.interpolation)
    return [
        {
            "timeSeconds": float(key.time),
            "value": bool(key.value) if boolean else float(key.value),
            "interpolation": interpolation,
            **(
                {
                    "forward": float(key.forward),
                    "backward": float(key.backward),
                }
                if not boolean and hasattr(key, "forward")
                else {}
            ),
        }
        for key in group.keys
    ]


def _controller_values(blocks: list[object], system: object) -> tuple[float, list[dict[str, object]], dict[str, object]]:
    managers = [block for block in blocks if isinstance(block, NifFormat.NiControllerManager)]
    if len(managers) != 1 or len(managers[0].controller_sequences) != 1:
        raise UnsupportedParticleEffectError("particle graph requires one controller sequence")
    sequence = managers[0].controller_sequences[0]
    if int(sequence.cycle_type) != int(NifFormat.CycleType.CYCLELOOP):
        raise UnsupportedParticleEffectError("particle controller sequence is not looping")
    name = _text(system.name)
    birth = []
    active = []
    for controlled in sequence.controlled_blocks:
        if _text(controlled.node_name) != name or _text(controlled.controller_type) != "NiPSysEmitterCtlr":
            continue
        variable = _text(controlled.variable_2)
        if variable == "BirthRate":
            birth = _keys(controlled.interpolator)
        elif variable == "EmitterActive":
            active = _keys(controlled.interpolator, boolean=True)
    if len(birth) != 1 or not active:
        raise UnsupportedParticleEffectError(f"particle emitter controller is incomplete: {name}")
    emitter_controllers = [block for block in blocks
                           if isinstance(block, NifFormat.NiPSysEmitterCtlr)
                           and block.target is system]
    if len(emitter_controllers) != 1:
        raise UnsupportedParticleEffectError(f"particle emitter controller target is ambiguous: {name}")
    emitter_controller = emitter_controllers[0]
    return float(birth[0]["value"]), active, {
        "sequence": _text(sequence.name),
        "cycleMode": "loop",
        "frequency": float(sequence.frequency),
        "phaseSeconds": float(emitter_controller.phase),
        "startTimeSeconds": float(sequence.start_time),
        "stopTimeSeconds": float(sequence.stop_time),
    }


def _modifier_contract(modifier: object, root: object) -> dict[str, object] | None:
    common = {"type": type(modifier).__name__, "order": int(modifier.order), "active": bool(modifier.active)}
    if isinstance(modifier, (NifFormat.NiPSysBoxEmitter, NifFormat.NiPSysMeshEmitter)):
        return None
    if isinstance(modifier, _ENGINE_MODIFIERS):
        return None
    if isinstance(modifier, NifFormat.NiPSysSpawnModifier):
        return {**common, "percentageSpawned": float(modifier.percentage_spawned),
                "spawnCount": [int(modifier.min_num_to_spawn), int(modifier.max_num_to_spawn)],
                "speedChaos": float(modifier.spawn_speed_chaos),
                "lifeSeconds": _range(float(modifier.life_span), float(modifier.life_span_variation))}
    if isinstance(modifier, NifFormat.BSPSysSimpleColorModifier):
        return {**common, "fadeInPercent": float(modifier.fade_in_percent),
                "fadeOutPercent": float(modifier.fade_out_percent),
                "colorStops": [_color(value) for value in modifier.colors],
                "colorStopPercents": [float(modifier.color_1_end_percent),
                                      float(modifier.color_1_start_percent),
                                      float(modifier.color_2_end_percent),
                                      float(modifier.color_2_start_percent)]}
    if isinstance(modifier, NifFormat.NiPSysRotationModifier):
        return {**common, "speedRadiansPerSecond": _range(float(modifier.initial_rotation_speed),
                                                            float(modifier.initial_rotation_speed_variation)),
                "angleRadians": _range(float(modifier.initial_rotation_angle),
                                         float(modifier.initial_rotation_angle_variation)),
                "randomSpeedSign": bool(modifier.random_rot_speed_sign),
                "randomInitialAxis": bool(modifier.random_initial_axis),
                "initialAxisGodot": _vector(modifier.initial_axis)}
    if isinstance(modifier, NifFormat.NiPSysGrowFadeModifier):
        return {**common, "growSeconds": float(modifier.grow_time),
                "fadeSeconds": float(modifier.fade_time), "baseScale": float(modifier.base_scale)}
    if isinstance(modifier, NifFormat.NiPSysGravityModifier):
        matrix = modifier.gravity_object.get_transform(root)
        return {**common, "axisGodot": _vector(modifier.gravity_axis),
                "strength": float(modifier.strength), "turbulence": float(modifier.turbulence),
                "turbulenceScale": float(modifier.turbulence_scale),
                "originGodotUnits": [float(matrix.m_41), float(matrix.m_43), -float(matrix.m_42)]}
    if isinstance(modifier, NifFormat.NiPSysBombModifier):
        matrix = modifier.bomb_object.get_transform(root)
        return {**common, "axisGodot": _vector(modifier.bomb_axis),
                "decay": float(modifier.decay), "deltaVelocity": float(modifier.delta_v),
                "decayType": int(modifier.decay_type), "symmetryType": int(modifier.symmetry_type),
                "originGodotUnits": [float(matrix.m_41), float(matrix.m_43), -float(matrix.m_42)]}
    raise UnsupportedParticleEffectError(f"unsupported active particle modifier: {type(modifier).__name__}")


def _emitter_contract(emitter: object, root: object) -> dict[str, object]:
    common = {
        "type": type(emitter).__name__, "speedGameUnitsPerSecond": _range(float(emitter.speed), float(emitter.speed_variation)),
        "declinationRadians": _range(float(emitter.declination), float(emitter.declination_variation)),
        "planarAngleRadians": _range(float(emitter.planar_angle), float(emitter.planar_angle_variation)),
        "initialColor": _color(emitter.initial_color),
        "radiusGameUnits": _range(float(emitter.initial_radius), float(emitter.radius_variation)),
        "lifeSeconds": _range(float(emitter.life_span), float(emitter.life_span_variation)),
    }
    if isinstance(emitter, NifFormat.NiPSysBoxEmitter):
        matrix = emitter.emitter_object.get_transform(root)
        return {**common, "shape": "box", "extentsGameUnits": [float(emitter.width) / 2.0,
                                                                  float(emitter.depth) / 2.0,
                                                                  float(emitter.height) / 2.0],
                "originGodotUnits": [float(matrix.m_41), float(matrix.m_43), -float(matrix.m_42)]}
    if isinstance(emitter, NifFormat.NiPSysMeshEmitter):
        points: list[list[float]] = []
        normals: list[list[float]] = []
        triangles: list[list[int]] = []
        for mesh in emitter.emitter_meshes:
            matrix = mesh.get_transform(root)
            start = len(points)
            for vertex in mesh.data.vertices:
                transformed = vertex * matrix
                points.append([float(transformed.x), float(transformed.z), -float(transformed.y)])
            for normal in mesh.data.normals:
                transformed = normal * matrix.get_matrix_33()
                normals.append(_vector(transformed))
            triangles.extend([[start + int(a), start + int(c), start + int(b)]
                              for a, b, c in mesh.data.get_triangles()])
        if not points or not triangles:
            raise UnsupportedParticleEffectError("mesh emitter has no source triangles")
        return {**common, "shape": "mesh-surface", "pointsGodotUnits": points,
                "normalsGodot": normals, "triangles": triangles,
                "emissionType": int(emitter.emission_type), "emissionAxisGodot": _vector(emitter.emission_axis)}
    raise UnsupportedParticleEffectError(f"unsupported particle emitter: {type(emitter).__name__}")


def compile_particle_effect(source_bytes: bytes, logical_path: str) -> dict[str, object]:
    decoded = decode_nif(source_bytes)
    data = decoded.document
    blocks = list(dict.fromkeys(data.get_global_iterator()))
    if len(data.roots) != 1:
        raise UnsupportedParticleEffectError("particle NIF requires one root")
    root = data.roots[0]
    systems = [block for block in blocks if isinstance(block, NifFormat.NiParticleSystem)]
    if not systems:
        raise UnsupportedParticleEffectError("owned NIF has no particle systems")
    rows = []
    for system in systems:
        emitters = [modifier for modifier in system.modifiers
                    if isinstance(modifier, (NifFormat.NiPSysBoxEmitter, NifFormat.NiPSysMeshEmitter))]
        if len(emitters) != 1 or system.data is None:
            raise UnsupportedParticleEffectError("particle system requires one supported emitter and data")
        birth_rate, active_keys, controller = _controller_values(blocks, system)
        shader = next((prop for prop in system.properties
                       if isinstance(prop, NifFormat.BSShaderNoLightingProperty)), None)
        alpha = next((prop for prop in system.properties if isinstance(prop, NifFormat.NiAlphaProperty)), None)
        if shader is None or not _text(shader.file_name):
            raise UnsupportedParticleEffectError("particle system requires an owned effect texture")
        modifiers = [row for row in (_modifier_contract(value, root) for value in system.modifiers) if row]
        rows.append({
            "name": _text(system.name), "worldSpace": bool(system.world_space),
            "maximumParticles": int(system.data.bs_max_vertices), "birthRatePerSecond": birth_rate,
            "activeKeys": active_keys, "controller": controller,
            "texturePath": _text(shader.file_name).replace("/", "\\").lower(),
            "alphaFlags": int(alpha.flags) if alpha is not None else 0,
            "alphaThreshold": int(alpha.threshold) if alpha is not None else 0,
            "emitter": _emitter_contract(emitters[0], root),
            "modifiers": sorted(modifiers, key=lambda value: int(value["order"])),
        })
    return {"schema": PARTICLE_SCHEMA, "status": "source-particle-graph", "systems": rows}


def export_particle_nif(source: Path, logical_path: str, gltf_path: Path, sidecar_path: Path) -> dict[str, object]:
    source_bytes = source.read_bytes()
    source_hash = sha256_bytes(source_bytes)
    effect = compile_particle_effect(source_bytes, logical_path)
    binary_name = gltf_path.with_suffix(".bin").name
    gltf = {"asset": {"version": "2.0", "generator": GENERATOR}, "scene": 0,
            "scenes": [{"nodes": [0]}], "nodes": [{"name": Path(logical_path).stem}],
            "buffers": [{"uri": binary_name, "byteLength": 0}],
            "extras": {"openNvSchema": STATIC_SCHEMA, "sourceSha256": source_hash}}
    gltf_bytes = (json.dumps(gltf, indent=2, sort_keys=True) + "\n").encode()
    atomic_write(gltf_path.with_suffix(".bin"), b"")
    atomic_write(gltf_path, gltf_bytes)
    sidecar = {
        "schema": STATIC_SCHEMA, "status": "geometry-only",
        "source": {"logicalPath": logical_path.replace("/", "\\").lower(), "bytes": len(source_bytes),
                   "sha256": source_hash, "decoder": decode_nif(source_bytes).evidence()},
        "compiler": compiler_provenance("static"),
        "outputs": {"gltf": {"file": gltf_path.name, "bytes": len(gltf_bytes), "sha256": sha256_bytes(gltf_bytes)},
                    "buffer": {"file": binary_name, "bytes": 0, "sha256": sha256_bytes(b"")}},
        "coverage": {"surfaces": 0, "collisionExported": False, "collisionBlockTypes": [],
                     "collisionUnsupportedReason": "particle-presentation-has-no-authored-collision",
                     "collisionBodies": [],
                     "dynamicPhysicsExported": False, "dynamicPhysicsUnsupportedReasons": [],
                     "dynamicPhysicsBodies": [], "controllers": []},
        "surfaces": [], "particleEffect": effect,
    }
    atomic_write(sidecar_path, (json.dumps(sidecar, indent=2, sort_keys=True) + "\n").encode())
    return sidecar
