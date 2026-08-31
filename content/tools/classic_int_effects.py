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
# Published classic INT format/opcode identities; operands still come from owned INT.
PUSH_STRING = 0x9001
PUSH_FLOAT = 0xA001
RANDOM = 0x80B4
SOURCE_OBJ = 0x80BD
SELF_OBJ = 0x80BC
DUDE_OBJ = 0x80BF
LOCAL_VAR = 0x80C1
SET_LOCAL_VAR = 0x80C2
MAP_VAR = 0x80C3
GLOBAL_VAR = 0x80C5
GET_CRITTER_STAT = 0x80CA
EQUAL = 0x8033
AND = 0x803E
IF = 0x802F
CAN_SEE = 0x80DC
ATTACK = 0x80D0
JUMP = 0x8004
DISPLAY_MSG = 0x80B8
SCRIPT_OVERRIDES = 0x80B9
MESSAGE_STR = 0x8105
METARULE = 0x810B
COMBAT_DIFFICULTY = 0x814F
SFALL_ARRAY_LENGTH = 0x8231
DIFFICULTY_LEVEL = 0x812A
ADD = 0x8039
NEGATE = 0x8046
FETCH_PROGRAM_VARIABLE = 0x8012
FETCH_LOCAL_VARIABLE = 0x8032
NOT_EQUAL = 0x8034
GREATER_THAN_OR_EQUAL = 0x8036
LESS_THAN = 0x8037
SUBTRACT = 0x803A
MULTIPLY = 0x803B
DIVIDE = 0x803C
MODULO = 0x803D
NOT = 0x8045
BITWISE_AND = 0x8040
OR = 0x803F
DUDE_NAME = 0x80A4
GSAY_REPLY = 0x811E
GIQ_OPTION = 0x8121
OBJECT_ART_FID = 0x8149
START_GDIALOG = 0x80DE
GSAY_START = 0x811C
CALL = 0x8005
RESTORE_RETURN = 0x801A
GSAY_END = 0x811D
END_DIALOGUE = 0x80DF
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
TALK_ENTRY_ART_COUNT = 2


class ClassicIntDecodeError(ValueError):
    pass


@dataclass(frozen=True)
class Instruction:
    offset: int
    opcode: int
    operand: int | None = None


_EXPRESSION_OPERATIONS = {
    FETCH_PROGRAM_VARIABLE: ("program-variable", 1),
    FETCH_LOCAL_VARIABLE: ("local-variable", 1),
    LOCAL_VAR: ("script-local-variable", 1),
    MAP_VAR: ("map-variable", 1),
    GLOBAL_VAR: ("global-variable", 1),
    SELF_OBJ: ("self-object", 0),
    DUDE_OBJ: ("dude-object", 0),
    GET_CRITTER_STAT: ("critter-stat", 2),
    METARULE: ("metarule", 2),
    COMBAT_DIFFICULTY: ("combat-difficulty", 0),
    SFALL_ARRAY_LENGTH: ("sfall-array-length", 1),
    DIFFICULTY_LEVEL: ("difficulty-level", 0),
    EQUAL: ("equal", 2),
    NOT_EQUAL: ("not-equal", 2),
    GREATER_THAN_OR_EQUAL: ("greater-than-or-equal", 2),
    LESS_THAN: ("less-than", 2),
    ADD: ("add", 2),
    SUBTRACT: ("subtract", 2),
    MULTIPLY: ("multiply", 2),
    DIVIDE: ("divide", 2),
    MODULO: ("modulo", 2),
    AND: ("and", 2),
    OR: ("or", 2),
    BITWISE_AND: ("bitwise-and", 2),
    NOT: ("not", 1),
    NEGATE: ("negate", 1),
    RANDOM: ("random-inclusive", 2),
}


def _source_expression(
    instructions: list[Instruction],
    end: int,
) -> tuple[dict[str, Any], int]:
    if end < 0:
        raise ClassicIntDecodeError("INT expression exhausted the procedure stack")
    instruction = instructions[end]
    if instruction.opcode == PUSH_INT:
        return {
            "kind": "literal",
            "offset": instruction.offset,
            "value": instruction.operand,
            "arguments": [],
        }, end
    operation = _EXPRESSION_OPERATIONS.get(instruction.opcode)
    if operation is None:
        raise ClassicIntDecodeError(
            f"unsupported INT expression opcode 0x{instruction.opcode:04x} "
            f"at 0x{instruction.offset:x}"
        )
    kind, arity = operation
    arguments = []
    start = end
    for _ in range(arity):
        argument, start = _source_expression(instructions, start - 1)
        arguments.append(argument)
    arguments.reverse()
    return {
        "kind": kind,
        "offset": instruction.offset,
        "value": None,
        "arguments": arguments,
    }, start


def _bounded_expression(
    instructions: list[Instruction],
    end: int,
) -> tuple[dict[str, Any] | None, int | None, str | None]:
    try:
        expression, start = _source_expression(instructions, end)
        return expression, start, None
    except ClassicIntDecodeError as error:
        return None, None, str(error)


def _u32(data: bytes, offset: int, label: str) -> int:
    if offset < 0 or offset + 4 > len(data):
        raise ClassicIntDecodeError(f"truncated INT {label} at 0x{offset:x}")
    return struct.unpack_from(">I", data, offset)[0]


def _procedure_inventory(
    data: bytes,
    require_bounded_signature: bool,
) -> dict[str, dict[str, int | tuple[int, int]]]:
    count = _u32(data, PROCEDURE_TABLE_OFFSET, "procedure count")
    if count == 0 or count > MAX_PROCEDURES:
        raise ClassicIntDecodeError(f"invalid INT procedure count: {count}")
    table = PROCEDURE_TABLE_OFFSET + 4
    identifiers = table + count * PROCEDURE_SIZE
    identifier_bytes = _u32(data, identifiers, "identifier table size")
    identifier_end = identifiers + 4 + identifier_bytes
    if identifier_end > len(data):
        raise ClassicIntDecodeError("INT identifier table exceeds the program")
    rows: list[tuple[str, int, int, int, int, int]] = []
    for index in range(count):
        row = table + index * PROCEDURE_SIZE
        name_offset, flags, time, condition, body, arguments = struct.unpack_from(
            ">6I", data, row
        )
        if require_bounded_signature and (
            flags != 0 or time != 0 or condition != 0 or arguments != 0
        ):
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
        rows.append((name, flags, time, condition, body, arguments))
    ordered_offsets = sorted({row[4] for row in rows} | {len(data)})
    result: dict[str, dict[str, int | tuple[int, int]]] = {}
    for name, flags, time, condition, body, arguments in rows:
        end = next(offset for offset in ordered_offsets if offset > body)
        if name in result:
            raise ClassicIntDecodeError(f"duplicate INT procedure: {name}")
        result[name] = {
            "flags": flags,
            "time": time,
            "condition": condition,
            "arguments": arguments,
            "bounds": (body, end),
        }
    return result


def _procedures(data: bytes) -> dict[str, tuple[int, int]]:
    return {
        name: row["bounds"]
        for name, row in _procedure_inventory(data, True).items()
    }


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
        if opcode in {PUSH_STRING, PUSH_FLOAT, PUSH_INT}:
            if offset + 4 > end:
                raise ClassicIntDecodeError("truncated INT integer push")
            operand = struct.unpack_from(">i", data, offset)[0]
            offset += 4
        result.append(Instruction(instruction_offset, opcode, operand))
    return result


def inventory_int_program(data: bytes) -> dict[str, Any]:
    """Inventory source procedures and RANDOM operand shapes without executing them."""
    procedures = _procedure_inventory(data, False)
    rows: list[dict[str, Any]] = []
    random_sites: list[dict[str, Any]] = []
    for name, procedure in procedures.items():
        bounds = procedure["bounds"]
        instructions = _instructions(data, bounds)
        branches = []
        for index, instruction in enumerate(instructions):
            if instruction.opcode not in {IF, JUMP}:
                continue
            condition = None
            condition_start = index
            condition_error = None
            if instruction.opcode == IF:
                condition, condition_start, condition_error = _bounded_expression(
                    instructions, index - 1
                )
            if condition_start is None:
                target = None
                target_error = "INT branch target follows an unsupported condition"
            else:
                target, _, target_error = _bounded_expression(
                    instructions, condition_start - 1
                )
            branches.append(
                {
                    "offset": instruction.offset,
                    "kind": "conditional" if instruction.opcode == IF else "jump",
                    "targetKind": "source-expression",
                    "target": target,
                    "condition": condition,
                    "expressionStatus": (
                        "executable"
                        if target_error is None and condition_error is None
                        else "unsupported"
                    ),
                    "unsupported": target_error or condition_error,
                }
            )
        sites: list[dict[str, Any]] = []
        for index, instruction in enumerate(instructions):
            if instruction.opcode != RANDOM:
                continue
            lower = instructions[index - 2] if index >= 2 else None
            upper = instructions[index - 1] if index >= 1 else None
            literal = (
                lower is not None
                and upper is not None
                and lower.opcode == PUSH_INT
                and upper.opcode == PUSH_INT
            )
            maximum_expression, maximum_start, maximum_error = _bounded_expression(
                instructions, index - 1
            )
            minimum_expression = None
            minimum_error = None
            if maximum_start is not None:
                minimum_expression, _, minimum_error = _bounded_expression(
                    instructions, maximum_start - 1
                )
            site = {
                "procedure": name,
                "offset": instruction.offset,
                "operandKind": (
                    "literal-inclusive-range"
                    if literal
                    else "source-stack-expression"
                ),
                "minimum": lower.operand if literal else None,
                "maximum": upper.operand if literal else None,
                "minimumExpression": minimum_expression,
                "maximumExpression": maximum_expression,
                "expressionStatus": (
                    "executable"
                    if maximum_error is None and minimum_error is None
                    else "unsupported"
                ),
                "unsupported": maximum_error or minimum_error,
            }
            sites.append(site)
            random_sites.append(site)
        rows.append(
            {
                "name": name,
                "bodyOffset": bounds[0],
                "bodyEndOffset": bounds[1],
                "flags": procedure["flags"],
                "time": procedure["time"],
                "conditionOffset": procedure["condition"],
                "arguments": procedure["arguments"],
                "eventKind": (
                    "program-start"
                    if name == "start"
                    else "map-enter"
                    if name == "map_enter_p_proc"
                    else "object-event"
                    if name.endswith("_p_proc")
                    else "helper"
                ),
                "instructionCount": len(instructions),
                "canonicalEpilogueOffset": (
                    instructions[-len(EPILOGUE)].offset
                    if len(instructions) >= len(EPILOGUE)
                    and all(
                        instruction.opcode == opcode
                        and (operand is None or instruction.operand == operand)
                        for instruction, (opcode, operand) in zip(
                            instructions[-len(EPILOGUE):], EPILOGUE
                        )
                    )
                    else None
                ),
                "instructions": [
                    {
                        "offset": instruction.offset,
                        "opcode": f"{instruction.opcode:04x}",
                        "operand": instruction.operand,
                    }
                    for instruction in instructions
                ],
                "branches": branches,
                "randomSites": sites,
            }
        )
    return {
        "schema": "opennv-classic-int-initialization-inventory/v3",
        "procedures": rows,
        "randomSites": random_sites,
        "randomOpcode": f"{RANDOM:04x}",
    }


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


def _decode_look_at(data: bytes, bounds: tuple[int, int]) -> tuple[int, int, int, int]:
    code = _instructions(data, bounds)
    cursor = 0

    def take(opcode: int, operand: int | None = None) -> Instruction:
        nonlocal cursor
        instruction = _expect(code, cursor, opcode, operand)
        cursor += 1
        return instruction

    take(PUSH_BASE)
    take(SCRIPT_OVERRIDES)
    else_branch = take(PUSH_INT)
    local = take(PUSH_INT).operand
    take(LOCAL_VAR)
    take(PUSH_INT, 0)
    take(EQUAL)
    take(IF)
    set_local = take(PUSH_INT).operand
    take(PUSH_INT, 1)
    take(SET_LOCAL_VAR)
    message_list = take(PUSH_INT).operand
    first_message = take(PUSH_INT).operand
    take(MESSAGE_STR)
    take(DISPLAY_MSG)
    end_branch = take(PUSH_INT)
    take(JUMP)
    if cursor >= len(code) or else_branch.operand != code[cursor].offset:
        raise ClassicIntDecodeError("INT look-at else target is invalid")
    repeat_list = take(PUSH_INT).operand
    repeat_message = take(PUSH_INT).operand
    take(MESSAGE_STR)
    take(DISPLAY_MSG)
    if (
        cursor >= len(code)
        or end_branch.operand != code[cursor].offset
        or local is None
        or local < 0
        or set_local != local
        or message_list is None
        or message_list < 0
        or repeat_list != message_list
        or first_message is None
        or first_message < 0
        or repeat_message is None
        or repeat_message < 0
    ):
        raise ClassicIntDecodeError("INT look-at state or message identity is invalid")
    _expect_epilogue(code, cursor)
    return local, message_list, first_message, repeat_message


def _decode_dialogue_node(
    data: bytes,
    bounds: tuple[int, int],
    procedure_names: list[str],
) -> list[dict[str, Any]]:
    code = _instructions(data, bounds)
    cursor = 0

    def take(opcode: int, operand: int | None = None) -> Instruction:
        nonlocal cursor
        instruction = _expect(code, cursor, opcode, operand)
        cursor += 1
        return instruction

    take(PUSH_BASE)
    effects: list[dict[str, Any]] = []
    first = take(PUSH_INT).operand
    if cursor + 1 < len(code) and code[cursor + 1].opcode == PUSH_INT:
        message_list = take(PUSH_INT).operand
        first_message = take(PUSH_INT).operand
        take(MESSAGE_STR)
        take(DUDE_OBJ)
        take(DUDE_NAME)
        take(ADD)
        repeat_list = take(PUSH_INT).operand
        second_message = take(PUSH_INT).operand
        take(MESSAGE_STR)
        take(ADD)
        take(GSAY_REPLY)
        if first != message_list or repeat_list != message_list:
            raise ClassicIntDecodeError("INT dialogue reply message-list identity drifted")
        effects.extend([
            {
                "operation": "dialogue-reply-message",
                "messageListId": message_list,
                "messageId": first_message,
            },
            {"operation": "dialogue-reply-player-name"},
            {
                "operation": "dialogue-reply-message",
                "messageListId": message_list,
                "messageId": second_message,
            },
        ])
    else:
        message_id = take(PUSH_INT).operand
        take(GSAY_REPLY)
        message_list = first
        effects.append({
            "operation": "dialogue-reply-message",
            "messageListId": message_list,
            "messageId": message_id,
        })
    while len(code) - cursor > len(EPILOGUE):
        intelligence = take(PUSH_INT).operand
        if cursor < len(code) and code[cursor].opcode == NEGATE:
            take(NEGATE)
            intelligence = -intelligence if intelligence is not None else None
        option_list = take(PUSH_INT).operand
        option_message = take(PUSH_INT).operand
        target_index = take(PUSH_INT).operand
        reaction = take(PUSH_INT).operand
        take(GIQ_OPTION)
        if (
            intelligence is None
            or option_list != message_list
            or option_message is None
            or target_index is None
            or target_index < 0
            or target_index >= len(procedure_names)
            or reaction is None
        ):
            raise ClassicIntDecodeError("INT dialogue option identity is invalid")
        target = procedure_names[target_index]
        if not target.startswith("Node"):
            raise ClassicIntDecodeError("INT dialogue option target is not a node")
        option: dict[str, Any] = {
            "operation": "dialogue-option",
            "messageListId": option_list,
            "messageId": option_message,
            "target": target,
            "reaction": reaction,
        }
        option["minimumIntelligence" if intelligence >= 0 else "maximumIntelligence"] = abs(
            intelligence
        )
        effects.append(option)
    _expect_epilogue(code, cursor)
    return effects


def _decode_talk_entry(
    data: bytes,
    bounds: tuple[int, int],
    procedure_names: list[str],
) -> tuple[list[str], str]:
    code = _instructions(data, bounds)
    candidates = [
        index for index in range(2, len(code))
        if code[index].opcode == OBJECT_ART_FID
        and code[index - 1].opcode == DUDE_OBJ
    ]
    if len(candidates) < TALK_ENTRY_ART_COUNT:
        raise ClassicIntDecodeError("INT talk entry art branch is absent")
    cursor = candidates[-TALK_ENTRY_ART_COUNT] - 2

    def take(opcode: int, operand: int | None = None) -> Instruction:
        nonlocal cursor
        instruction = _expect(code, cursor, opcode, operand)
        cursor += 1
        return instruction

    else_branch = take(PUSH_INT)
    art_fids: list[str] = []
    for _ in range(TALK_ENTRY_ART_COUNT):
        take(DUDE_OBJ)
        take(OBJECT_ART_FID)
        art = take(PUSH_INT).operand
        take(EQUAL)
        if art is None or art < 0:
            raise ClassicIntDecodeError("INT talk entry art identity is invalid")
        art_fids.append(f"{art:08x}")
    take(OR)
    take(IF)

    def dialogue_call() -> str:
        take(PUSH_INT)
        take(SELF_OBJ)
        take(PUSH_INT)
        take(PUSH_INT)
        take(NEGATE)
        take(PUSH_INT)
        take(NEGATE)
        take(START_GDIALOG)
        take(GSAY_START)
        take(PUSH_INT)
        take(D_TO_A)
        take(PUSH_INT, 0)
        target_index = take(PUSH_INT).operand
        take(CALL)
        take(RESTORE_RETURN)
        take(GSAY_END)
        take(END_DIALOGUE)
        if target_index is None or target_index >= len(procedure_names):
            raise ClassicIntDecodeError("INT talk entry node target is invalid")
        return procedure_names[target_index]

    entry_node = dialogue_call()
    end_branch = take(PUSH_INT)
    take(JUMP)
    if cursor >= len(code) or else_branch.operand != code[cursor].offset:
        raise ClassicIntDecodeError("INT talk entry else target is invalid")
    dialogue_call()
    if cursor >= len(code) or end_branch.operand != code[cursor].offset:
        raise ClassicIntDecodeError("INT talk entry end target is invalid")
    _expect_epilogue(code, cursor)
    if not entry_node.startswith("Node"):
        raise ClassicIntDecodeError("INT talk entry does not call a dialogue node")
    return art_fids, entry_node


def decode_acklint_effects(data: bytes) -> dict[str, Any]:
    procedures = _procedures(data)
    procedure_names = list(procedures)
    try:
        pickup = _decode_pickup(data, procedures["pickup_p_proc"])
        critter = _decode_critter(data, procedures["critter_p_proc"])
        look_at = _decode_look_at(data, procedures["look_at_p_proc"])
        player_art_fids, initial_node = _decode_talk_entry(
            data, procedures["talk_p_proc"], procedure_names
        )
        dialogue_nodes: dict[str, list[dict[str, Any]]] = {}
        pending = [initial_node]
        terminal_nodes: set[str] = set()
        while pending:
            name = pending.pop(0)
            if name in dialogue_nodes or name in terminal_nodes:
                continue
            bounds = procedures.get(name)
            if bounds is None:
                raise ClassicIntDecodeError(f"INT dialogue node is absent: {name}")
            instructions = _instructions(data, bounds)
            if (
                len(instructions) == len(EPILOGUE) + 1
                and instructions[0].opcode == PUSH_BASE
            ):
                _expect_epilogue(instructions, 1)
                terminal_nodes.add(name)
                dialogue_nodes[name] = [{"operation": "dialogue-end"}]
                continue
            effects = _decode_dialogue_node(data, bounds, procedure_names)
            dialogue_nodes[name] = effects
            pending.extend(
                operation["target"]
                for operation in effects
                if operation["operation"] == "dialogue-option"
            )
        if len(terminal_nodes) != 1:
            raise ClassicIntDecodeError("INT dialogue graph has no unique terminal node")
    except KeyError as error:
        raise ClassicIntDecodeError(
            f"required ACKlint INT procedure is absent: {error.args[0]}"
        ) from error
    if pickup[0] != critter[0]:
        raise ClassicIntDecodeError("ACKlint procedures do not share one local variable")
    local, pickup_value = pickup
    _, required_value, attack_value = critter
    look_local, message_list, first_message, repeat_message = look_at
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
            "look_at_p_proc": [
                {
                    "all": [{
                        "operation": "local-equals",
                        "index": look_local,
                        "value": 0,
                    }],
                    "then": [
                        {
                            "operation": "set-local",
                            "index": look_local,
                            "value": 1,
                        },
                        {"operation": "script-overrides"},
                        {
                            "operation": "display-message",
                            "messageListId": message_list,
                            "messageId": first_message,
                        },
                    ],
                },
                {
                    "all": [{
                        "operation": "local-not-equals",
                        "index": look_local,
                        "value": 0,
                    }],
                    "then": [
                        {"operation": "script-overrides"},
                        {
                            "operation": "display-message",
                            "messageListId": message_list,
                            "messageId": repeat_message,
                        },
                    ],
                },
            ],
            "talk_p_proc": [{
                "all": [{
                    "operation": "player-art-fid-in",
                    "values": player_art_fids,
                }],
                "then": [{"operation": "open-dialogue", "node": initial_node}],
            }],
            **{
                node: [{"all": [], "then": effects}]
                for node, effects in dialogue_nodes.items()
            },
        },
    }
