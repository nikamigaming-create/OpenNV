"""Parse the bounded SSL effect source admitted for the Fallout 1 flare."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any


class ClassicSslParseError(ValueError):
    pass


CLASSIC_SSL_SOURCE_CONTRACT_HOURS = 2
CLASSIC_SSL_SOURCE_CONTRACT_MINUTES_PER_HOUR = 60
CLASSIC_SSL_SOURCE_CONTRACT_SECONDS_PER_MINUTE = 60
CLASSIC_SSL_SOURCE_CONTRACT_TICKS_PER_SECOND = 10
CLASSIC_SSL_SOURCE_CONTRACT_LOOK_TOKEN_COUNT = 10
CLASSIC_SSL_SOURCE_CONTRACT_LOOK_MESSAGE_INDEX = 6
CLASSIC_SSL_SOURCE_CONTRACT_LOOK_TAIL_INDEX = 7
CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_REPLY_MESSAGE_INDEX = 2
CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_OPTION_MESSAGE_INDEX = 7
CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_OPTION_TARGET_INDEX = 9
CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_REACTION_INDEX = 11


@dataclass(frozen=True)
class Token:
    text: str
    offset: int


def _tokens(source: str) -> list[Token]:
    result: list[Token] = []
    offset = 0
    while offset < len(source):
        if source[offset].isspace():
            offset += 1
            continue
        if source.startswith("//", offset):
            newline = source.find("\n", offset + 2)
            offset = len(source) if newline < 0 else newline + 1
            continue
        if source[offset] == "#":
            newline = source.find("\n", offset + 1)
            offset = len(source) if newline < 0 else newline + 1
            continue
        if source.startswith("/*", offset):
            end = source.find("*/", offset + 2)
            if end < 0:
                raise ClassicSslParseError("unterminated SSL block comment")
            offset = end + 2
            continue
        start = offset
        if source[offset] == '"':
            offset += 1
            while offset < len(source) and source[offset] != '"':
                if source[offset] == "\\":
                    offset += 1
                offset += 1
            if offset >= len(source):
                raise ClassicSslParseError("unterminated SSL string")
            offset += 1
        elif source[offset].isalpha() or source[offset] == "_":
            offset += 1
            while offset < len(source) and (
                source[offset].isalnum() or source[offset] == "_"
            ):
                offset += 1
        elif source[offset].isdigit():
            offset += 1
            while offset < len(source) and source[offset].isdigit():
                offset += 1
        elif source[offset:offset + 2] in {":=", "==", "!=", ">=", "<="}:
            offset += 2
        elif source[offset] in "()*,;+-><":
            offset += 1
        else:
            offset += 1
        result.append(Token(source[start:offset], start))
    return result


def _find_all(tokens: list[Token], pattern: tuple[str, ...]) -> list[int]:
    return [
        index
        for index in range(len(tokens) - len(pattern) + 1)
        if tuple(token.text.casefold() for token in tokens[index:index + len(pattern)])
        == pattern
    ]


def _block(tokens: list[Token], begin_index: int) -> list[Token]:
    if tokens[begin_index].text.casefold() != "begin":
        raise ClassicSslParseError("SSL block does not start with begin")
    depth = 1
    for index in range(begin_index + 1, len(tokens)):
        text = tokens[index].text.casefold()
        if text == "begin":
            depth += 1
        elif text == "end":
            depth -= 1
            if depth == 0:
                return tokens[begin_index + 1:index]
    raise ClassicSslParseError("unterminated SSL begin/end block")


def _unique_integer_call(
    tokens: list[Token],
    name: str,
    tail: tuple[str, ...],
) -> int:
    matches: list[int] = []
    folded = [token.text.casefold() for token in tokens]
    for index in range(len(tokens) - len(tail) - 3):
        if folded[index] != name or folded[index + 1] != "(":
            continue
        if not tokens[index + 2].text.isdecimal():
            continue
        if tuple(folded[index + 3:index + 3 + len(tail)]) == tail:
            matches.append(int(tokens[index + 2].text))
    if len(matches) != 1:
        raise ClassicSslParseError(f"SSL {name} bounded call is not unique")
    return matches[0]


def decode_flare_effects(source: str) -> tuple[dict[str, Any], dict[str, Any]]:
    tokens = _tokens(source)
    use_header = (
        "if", "(", "script_action", "==", "use_proc", ")", "then", "begin"
    )
    use_blocks = [_block(tokens, index + len(use_header) - 1)
                  for index in _find_all(tokens, use_header)]
    candidates: list[tuple[list[Token], int]] = []
    for block in use_blocks:
        try:
            local = _unique_integer_call(
                block,
                "set_local_var",
                (",", "game_time", ")", ";"),
            )
        except ClassicSslParseError:
            continue
        candidates.append((block, local))
    if len(candidates) != 1:
        raise ClassicSslParseError("SSL flare use procedure is not unique")
    use_block, local = candidates[0]
    lit_assignments = [
        int(use_block[index + 2].text)
        for index in range(len(use_block) - 3)
        if use_block[index].text.casefold() == "lit"
        and use_block[index + 1].text == ":="
        and use_block[index + 2].text.isdecimal()
        and use_block[index + 3].text == ";"
    ]
    if lit_assignments != [1]:
        raise ClassicSslParseError("SSL flare lit assignment is unsupported")

    expiry_pattern = (
        "game_time", "-", "local_var", "(", str(local), ")", ")", ">", "(",
        "2", "*", "60", "*", "60", "*", "10", ")",
    )
    start_header = (
        "if", "(", "script_action", "==", "start_proc", ")", "then", "begin"
    )
    destroy_pattern = ("destroy_object", "(", "self_obj", ")", ";")
    expiry_blocks = [
        block
        for block in (
            _block(tokens, index + len(start_header) - 1)
            for index in _find_all(tokens, start_header)
        )
        if len(_find_all(block, expiry_pattern)) == 1
        and len(_find_all(block, destroy_pattern)) == 1
    ]
    if len(expiry_blocks) != 1:
        raise ClassicSslParseError("SSL flare expiry expression is unsupported")
    program = {
        "schema": "opennv-classic-script-effects/v1",
        "events": {
            "use_proc": [
                {
                    "all": [{
                        "operation": "local-equals",
                        "index": local,
                        "value": 0,
                    }],
                    "then": [{
                        "operation": "set-local",
                        "index": local,
                        "valueFrom": "game-time",
                    }],
                },
                {
                    "all": [],
                    "then": [{"operation": "set-flag", "flag": "lit"}],
                },
            ],
        },
    }
    expiry = {
        "operation": "elapsed-game-time-greater-than",
        "localIndex": local,
        "durationGameTicks": (
            CLASSIC_SSL_SOURCE_CONTRACT_HOURS
            * CLASSIC_SSL_SOURCE_CONTRACT_MINUTES_PER_HOUR
            * CLASSIC_SSL_SOURCE_CONTRACT_SECONDS_PER_MINUTE
            * CLASSIC_SSL_SOURCE_CONTRACT_TICKS_PER_SECOND
        ),
        "result": "destroy-self",
        "runtime": "unimplemented-fail-closed",
    }
    return program, expiry


def decode_single_message_look(source: str) -> dict[str, Any]:
    tokens = _tokens(source)
    header = ("procedure", "look_at_p_proc", "begin")
    blocks = [
        _block(tokens, index + len(header) - 1)
        for index in _find_all(tokens, header)
    ]
    if len(blocks) != 1:
        raise ClassicSslParseError("SSL single-message look procedure is not unique")
    expected = (
        "script_overrides", ";", "display_msg", "(", "mstr", "(",
    )
    candidates: list[int] = []
    for block in blocks:
        folded = tuple(token.text.casefold() for token in block)
        if (
            len(folded) != CLASSIC_SSL_SOURCE_CONTRACT_LOOK_TOKEN_COUNT
            or folded[:CLASSIC_SSL_SOURCE_CONTRACT_LOOK_MESSAGE_INDEX] != expected
            or folded[CLASSIC_SSL_SOURCE_CONTRACT_LOOK_TAIL_INDEX:] != (")", ")", ";")
        ):
            continue
        message = block[CLASSIC_SSL_SOURCE_CONTRACT_LOOK_MESSAGE_INDEX].text
        if message.isdecimal():
            candidates.append(int(message))
    if len(candidates) != 1:
        raise ClassicSslParseError("SSL single-message look procedure is unsupported")
    return {
        "schema": "opennv-classic-script-effects/v1",
        "events": {
            "look_at_p_proc": [{
                "all": [],
                "then": [
                    {"operation": "script-overrides"},
                    {"operation": "display-message", "messageId": candidates[0]},
                ],
            }],
        },
    }


def decode_single_reply_option_dialogue(source: str, procedure: str) -> dict[str, Any]:
    tokens = _tokens(source)
    header = ("procedure", procedure.casefold(), "begin")
    blocks = [
        _block(tokens, index + len(header) - 1)
        for index in _find_all(tokens, header)
    ]
    if len(blocks) != 1:
        raise ClassicSslParseError("SSL single-reply dialogue procedure is not unique")
    folded = [token.text.casefold() for token in blocks[0]]
    expected = (
        "reply", "(", None, ")", ";", "noption", "(", None, ",",
        None, ",", None, ")", ";",
    )
    if len(folded) != len(expected) or any(
        wanted is not None and actual != wanted
        for actual, wanted in zip(folded, expected)
    ):
        raise ClassicSslParseError("SSL single-reply dialogue body is unsupported")
    numeric_indices = (
        CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_REPLY_MESSAGE_INDEX,
        CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_OPTION_MESSAGE_INDEX,
        CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_REACTION_INDEX,
    )
    if not all(blocks[0][index].text.isdecimal() for index in numeric_indices):
        raise ClassicSslParseError("SSL dialogue message or reaction is not numeric")
    target = blocks[0][CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_OPTION_TARGET_INDEX].text
    if not target or not (target[0].isalpha() or target[0] == "_"):
        raise ClassicSslParseError("SSL dialogue option target is invalid")
    return {
        "schema": "opennv-classic-script-effects/v1",
        "events": {
            procedure: [{
                "all": [],
                "then": [
                    {
                        "operation": "dialogue-reply-message",
                        "messageId": int(blocks[0][
                            CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_REPLY_MESSAGE_INDEX
                        ].text),
                    },
                    {
                        "operation": "dialogue-option",
                        "messageId": int(blocks[0][
                            CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_OPTION_MESSAGE_INDEX
                        ].text),
                        "target": target,
                        "reaction": int(blocks[0][
                            CLASSIC_SSL_SOURCE_CONTRACT_DIALOGUE_REACTION_INDEX
                        ].text),
                    },
                ],
            }],
        },
    }
