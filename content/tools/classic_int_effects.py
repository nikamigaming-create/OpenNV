"""Decode the bounded classic INT procedures admitted by OpenNV.

This is intentionally not a general INT virtual machine. It reads the published
procedure table and the exact stack/control-flow subset needed to transport the
ACKlint pickup and critter effects. Every other shape fails closed.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass
from typing import Any


PROCEDURE_TABLE_OFFSET = 42
PROCEDURE_SIZE = 24
MAX_PROCEDURES = 4096

PUSH_BASE = 0x802B
PUSH_INT = 0xC001
SOURCE_OBJ = 0x80BD
SELF_OBJ = 0x80BC
DUDE_OBJ = 0x80BF
LOCAL_VAR = 0x80C1
SET_LOCAL_VAR = 0x80C2
EQUAL = 0x8033
AND = 0x803E
IF = 0x802F
CAN_SEE = 0x80DC
ATTACK = 0x80D0
D_TO_A = 0x800D
SWAP_RETURN = 0x8019
POP_TO_BASE = 0x802A
POP_BASE = 0x8029
A_TO_D = 0x800C
POP_RETURN = 0x801C

EPILOGUE = (
    (PUSH_INT, 0),
    (D_TO_A, None),
    (SWAP_RETURN, None),
    (POP_TO_BASE, None),
    (POP_BASE, None),
    (A_TO_D, None),
    (POP_RETURN, None),
    (POP_TO_BASE, None),
    (POP_BASE, None),
    (POP_RETURN, None),
)
ATTACK_ARGUMENT_COUNT = 7


class ClassicIntDecodeError(ValueError):
    pass


@dataclass(frozen=True)
class Instruction:
    offset: int
    opcode: int
    operand: int | None = None


def _u32(data: bytes, offset: int, label: str) -> int:
    if offset < 0 or offset + 4 > len(data):
        raise ClassicIntDecodeError(f"truncated INT {label} at 0x{offset:x}")
    return struct.unpack_from(">I", data, offset)[0]


def _procedures(data: bytes) -> dict[str, tuple[int, int]]:
    count = _u32(data, PROCEDURE_TABLE_OFFSET, "procedure count")
    if count == 0 or count > MAX_PROCEDURES:
        raise ClassicIntDecodeError(f"invalid INT procedure count: {count}")
    table = PROCEDURE_TABLE_OFFSET + 4
    identifiers = table + count * PROCEDURE_SIZE
    identifier_bytes = _u32(data, identifiers, "identifier table size")
    identifier_end = identifiers + 4 + identifier_bytes
    if identifier_end > len(data):
        raise ClassicIntDecodeError("INT identifier table exceeds the program")
    rows: list[tuple[str, int]] = []
    for index in range(count):
        row = table + index * PROCEDURE_SIZE
        name_offset, flags, time, condition, body, arguments = struct.unpack_from(
            ">6I", data, row
        )
        if flags != 0 or time != 0 or condition != 0 or arguments != 0:
            raise ClassicIntDecodeError(
                "bounded INT decoder does not admit flagged or parameterized procedures"
            )
        name_start = identifiers + name_offset
        if name_start < identifiers + 4 or name_start >= identifier_end:
            raise ClassicIntDecodeError("INT procedure name offset is outside identifiers")
        name_end = data.find(b"\0", name_start, identifier_end)
        if name_end < 0:
            raise ClassicIntDecodeError("INT procedure name is unterminated")
        try:
            name = data[name_start:name_end].decode("ascii")
        except UnicodeDecodeError as error:
            raise ClassicIntDecodeError("INT procedure name is not ASCII") from error
        if not name or body < identifier_end or body >= len(data):
            raise ClassicIntDecodeError("INT procedure identity or body offset is invalid")
        rows.append((name, body))
    ordered_offsets = sorted({body for _, body in rows} | {len(data)})
    result: dict[str, tuple[int, int]] = {}
    for name, body in rows:
        end = next(offset for offset in ordered_offsets if offset > body)
        if name in result:
            raise ClassicIntDecodeError(f"duplicate INT procedure: {name}")
        result[name] = (body, end)
    return result


def _instructions(data: bytes, bounds: tuple[int, int]) -> list[Instruction]:
    offset, end = bounds
    result: list[Instruction] = []
    while offset < end:
        if offset + 2 > end:
            raise ClassicIntDecodeError("truncated INT opcode")
        instruction_offset = offset
        opcode = struct.unpack_from(">H", data, offset)[0]
        offset += 2
        operand = None
        if opcode == PUSH_INT:
            if offset + 4 > end:
                raise ClassicIntDecodeError("truncated INT integer push")
            operand = struct.unpack_from(">i", data, offset)[0]
            offset += 4
        result.append(Instruction(instruction_offset, opcode, operand))
    return result


def _expect(
    instructions: list[Instruction],
    index: int,
    opcode: int,
    operand: int | None = None,
) -> Instruction:
    if index >= len(instructions):
        raise ClassicIntDecodeError("INT procedure ended before its bounded effect")
    instruction = instructions[index]
    if instruction.opcode != opcode or operand is not None and instruction.operand != operand:
        raise ClassicIntDecodeError(
            f"unsupported INT opcode/control flow at 0x{instruction.offset:x}"
        )
    return instruction


def _expect_epilogue(instructions: list[Instruction], start: int) -> None:
    if len(instructions) - start != len(EPILOGUE):
        raise ClassicIntDecodeError("INT procedure has unsupported trailing control flow")
    for index, (opcode, operand) in enumerate(EPILOGUE, start):
        _expect(instructions, index, opcode, operand)


def _decode_pickup(data: bytes, bounds: tuple[int, int]) -> tuple[int, int]:
    code = _instructions(data, bounds)
    cursor = 0

    def take(opcode: int, operand: int | None = None) -> Instruction:
        nonlocal cursor
        instruction = _expect(code, cursor, opcode, operand)
        cursor += 1
        return instruction

    take(PUSH_BASE)
    branch = take(PUSH_INT)
    take(SOURCE_OBJ)
    take(DUDE_OBJ)
    take(EQUAL)
    take(IF)
    local = take(PUSH_INT).operand
    value = take(PUSH_INT).operand
    take(SET_LOCAL_VAR)
    if (
        cursor >= len(code)
        or branch.operand != code[cursor].offset
        or local is None
        or local < 0
        or value is None
    ):
        raise ClassicIntDecodeError("INT pickup branch target or local state is invalid")
    _expect_epilogue(code, cursor)
    return local, value


def _decode_critter(data: bytes, bounds: tuple[int, int]) -> tuple[int, int, int]:
    code = _instructions(data, bounds)
    cursor = 0

    def take(opcode: int, operand: int | None = None) -> Instruction:
        nonlocal cursor
        instruction = _expect(code, cursor, opcode, operand)
        cursor += 1
        return instruction

    take(PUSH_BASE)
    branch = take(PUSH_INT)
    local = take(PUSH_INT).operand
    take(LOCAL_VAR)
    required = take(PUSH_INT).operand
    take(EQUAL)
    take(SELF_OBJ)
    take(DUDE_OBJ)
    take(CAN_SEE)
    take(AND)
    take(IF)
    set_local = take(PUSH_INT).operand
    set_value = take(PUSH_INT).operand
    take(SET_LOCAL_VAR)
    take(DUDE_OBJ)
    for _ in range(ATTACK_ARGUMENT_COUNT):
        take(PUSH_INT)
    take(ATTACK)
    if (
        cursor >= len(code)
        or branch.operand != code[cursor].offset
        or local is None
        or local < 0
        or set_local != local
        or required is None
        or set_value is None
    ):
        raise ClassicIntDecodeError("INT critter branch target or local state is invalid")
    _expect_epilogue(code, cursor)
    return local, required, set_value


def decode_acklint_effects(data: bytes) -> dict[str, Any]:
    procedures = _procedures(data)
    try:
        pickup = _decode_pickup(data, procedures["pickup_p_proc"])
        critter = _decode_critter(data, procedures["critter_p_proc"])
    except KeyError as error:
        raise ClassicIntDecodeError(
            f"required ACKlint INT procedure is absent: {error.args[0]}"
        ) from error
    if pickup[0] != critter[0]:
        raise ClassicIntDecodeError("ACKlint procedures do not share one local variable")
    local, pickup_value = pickup
    _, required_value, attack_value = critter
    return {
        "schema": "opennv-classic-script-effects/v1",
        "events": {
            "pickup_proc": [{
                "all": [{"operation": "source-is-player"}],
                "then": [{
                    "operation": "set-local",
                    "index": local,
                    "value": pickup_value,
                }],
            }],
            "critter_proc": [{
                "all": [
                    {
                        "operation": "local-equals",
                        "index": local,
                        "value": required_value,
                    },
                    {"operation": "can-see-player"},
                ],
                "then": [
                    {
                        "operation": "set-local",
                        "index": local,
                        "value": attack_value,
                    },
                    {"operation": "set-flag", "flag": "attack-player-requested"},
                ],
            }],
        },
    }
