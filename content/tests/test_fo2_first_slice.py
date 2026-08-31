from __future__ import annotations

import hashlib
import json
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo2_first_slice import compile_fo2_first_slice  # noqa: E402
from fo2_profile import inspect_fo2_profile  # noqa: E402


def synthetic_dat2(members: list[tuple[str, bytes, bool]]) -> bytes:
    data = bytearray()
    rows = []
    for logical_path, decoded, compressed in sorted(members, key=lambda row: row[0].casefold()):
        stored = zlib.compress(decoded, level=9) if compressed else decoded
        rows.append((logical_path, compressed, len(decoded), len(stored), len(data)))
        data.extend(stored)
    tree = bytearray(struct.pack("<I", len(rows)))
    for logical_path, compressed, decoded_size, stored_size, offset in rows:
        encoded = logical_path.encode("utf-8")
        tree.extend(struct.pack("<I", len(encoded)))
        tree.extend(encoded)
        tree.extend(bytes((1 if compressed else 0,)))
        tree.extend(struct.pack("<III", decoded_size, stored_size, offset))
    final_size = len(data) + len(tree) + 8
    return bytes(data + tree + struct.pack("<II", len(tree), final_size))


def synthetic_frm() -> bytes:
    header = bytearray(0x3E)
    struct.pack_into(">IHHH", header, 0, 4, 10, 0, 1)
    struct.pack_into(">6h", header, 0x0A, 0, 0, 0, 0, 0, 0)
    struct.pack_into(">6h", header, 0x16, 0, 0, 0, 0, 0, 0)
    struct.pack_into(">6I", header, 0x22, 0, 0, 0, 0, 0, 0)
    frame = struct.pack(">HHIhh", 1, 1, 1, 0, 0) + b"\x00"
    struct.pack_into(">I", header, 0x3A, len(frame))
    return bytes(header) + frame


def synthetic_map() -> bytes:
    header = bytearray(0xEC)
    struct.pack_into(">i", header, 0, 20)
    header[4:20] = b"ARTEMPLE.MAP\0\0\0\0"
    struct.pack_into(">10i", header, 0x14, 18492, 0, 0, 0, 745, 12, 1, 0, 126, 0)
    tiles = struct.pack(">10000I", *([0x00010001] * 10000))
    scripts = struct.pack(">5i", 0, 0, 0, 0, 0)
    object_base = struct.pack(
        ">21i",
        1,
        18493,
        0,
        0,
        0,
        0,
        0,
        0,
        0x02000000,
        0,
        0,
        0x02000001,
        -1,
        0,
        0,
        0,
        -1,
        -1,
        0,
        0,
        0,
    )
    objects = struct.pack(">2i", 1, 1) + object_base + struct.pack(">3i", 0, 0, 0)
    return bytes(header) + tiles + scripts + objects


def synthetic_critter_pro() -> bytes:
    result = bytearray(0x1A0)
    struct.pack_into(">III", result, 0, 0x01000003, 300, 0x01000040)
    struct.pack_into(">3i", result, 0x20, -1, 1, 1)
    stats = [8, 5, 5, 5, 5, 8, 5, 50, 9, 8, 0, 3, 0, 15, 0, 5] + [0] * 19
    struct.pack_into(">35i", result, 0x30, *stats)
    struct.pack_into(">35i", result, 0xBC, *([0] * 35))
    return bytes(result)


def synthetic_weapon_pro() -> bytes:
    result = bytearray(122)
    struct.pack_into(">III", result, 0, 0x00000007, 700, 0x0000002A)
    struct.pack_into(">i", result, 0x20, 3)
    struct.pack_into(
        ">16i",
        result,
        0x39,
        4,
        3,
        10,
        0,
        2,
        8,
        0x05000007,
        4,
        4,
        6,
        1,
        -1,
        0,
        0,
        -1,
        0,
    )
    result[0x79] = 56
    return bytes(result)


def synthetic_acklint_int() -> bytes:
    push = lambda value: struct.pack(">Hi", 0xC001, value)
    opcode = lambda value: struct.pack(">H", value)
    epilogue = b"".join([
        push(0), opcode(0x800D), opcode(0x8019), opcode(0x802A),
        opcode(0x8029), opcode(0x800C), opcode(0x801C), opcode(0x802A),
        opcode(0x8029), opcode(0x801C),
    ])
    names = [
        "..............", "checkPartyMembersNearDoor", "start", "critter_p_proc",
        "pickup_p_proc", "talk_p_proc", "destroy_p_proc", "look_at_p_proc",
        "description_p_proc", "use_skill_on_p_proc", "damage_p_proc",
        "map_enter_p_proc", "Node998", "Node999", "Node001", "Node002",
        "Node003", "Node004", "Node005",
    ]
    identifiers = bytearray()
    name_offsets = []
    for name in names:
        name_offsets.append(4 + len(identifiers))
        identifiers.extend(name.encode("ascii") + b"\0")
    body_start = 42 + 4 + len(names) * 24 + 4 + len(identifiers)
    bodies_by_name: dict[str, bytes] = {
        name: opcode(0x8000) for name in names
    }
    critter_start = body_start + sum(
        len(bodies_by_name[name]) for name in names[:3]
    )
    critter_prefix_length = 2 + 6 + 6 + 2 + 6 + 2 + 2 + 2 + 2 + 2 + 2
    critter_effect_length = 6 + 6 + 2 + 2 + 7 * 6 + 2
    critter_epilogue = critter_start + critter_prefix_length + critter_effect_length
    critter = b"".join([
        opcode(0x802B), push(critter_epilogue), push(5), opcode(0x80C1),
        push(2), opcode(0x8033), opcode(0x80BC), opcode(0x80BF),
        opcode(0x80DC), opcode(0x803E), opcode(0x802F), push(5), push(1),
        opcode(0x80C2), opcode(0x80BF),
        *(push(value) for value in [0, 1, 0, 0, 30000, 0, 0]),
        opcode(0x80D0), epilogue,
    ])
    bodies_by_name["critter_p_proc"] = critter
    pickup_start = critter_start + len(critter)
    pickup_epilogue = pickup_start + 2 + 6 + 2 + 2 + 2 + 2 + 6 + 6 + 2
    pickup = b"".join([
        opcode(0x802B), push(pickup_epilogue), opcode(0x80BD), opcode(0x80BF),
        opcode(0x8033), opcode(0x802F), push(5), push(2), opcode(0x80C2),
        epilogue,
    ])
    bodies_by_name["pickup_p_proc"] = pickup

    def dialogue_call(target_index: int) -> bytes:
        return b"".join([
            push(751), opcode(0x80BC), push(4), push(1), opcode(0x8046),
            push(1), opcode(0x8046), opcode(0x80DE), opcode(0x811C), push(0),
            opcode(0x800D), push(0), push(target_index), opcode(0x8005),
            opcode(0x801A), opcode(0x811D), opcode(0x80DF),
        ])

    talk_start = pickup_start + len(pickup)
    first_call = dialogue_call(14)
    second_call = dialogue_call(14)
    talk_prefix_length = 6 + 2 + 2 + 6 + 2 + 2 + 2 + 6 + 2 + 2 + 2
    talk_else = talk_start + talk_prefix_length + len(first_call) + 8
    talk_end = talk_else + len(second_call)
    talk = b"".join([
        push(talk_else), opcode(0x80BF), opcode(0x8149), push(0x0100003E),
        opcode(0x8033), opcode(0x80BF), opcode(0x8149), push(0x0100003D),
        opcode(0x8033), opcode(0x803F), opcode(0x802F), first_call,
        push(talk_end), opcode(0x8004), second_call, epilogue,
    ])
    bodies_by_name["talk_p_proc"] = talk

    look_start = talk_start + len(talk) + len(bodies_by_name["destroy_p_proc"])
    look_else = look_start + 66
    look_epilogue = look_else + 16
    look = b"".join([
        opcode(0x802B), opcode(0x80B9), push(look_else), push(7),
        opcode(0x80C1), push(0), opcode(0x8033), opcode(0x802F),
        push(7), push(1), opcode(0x80C2), push(751), push(100),
        opcode(0x8105), opcode(0x80B8), push(look_epilogue), opcode(0x8004),
        push(751), push(101), opcode(0x8105), opcode(0x80B8), epilogue,
    ])
    bodies_by_name["look_at_p_proc"] = look

    def dialogue_node(
        reply_ids: tuple[int, ...],
        options: list[tuple[int, int, int, bool]],
    ) -> bytes:
        if len(reply_ids) == 2:
            reply = b"".join([
                push(751), push(751), push(reply_ids[0]), opcode(0x8105),
                opcode(0x80BF), opcode(0x80A4), opcode(0x8039), push(751),
                push(reply_ids[1]), opcode(0x8105), opcode(0x8039), opcode(0x811E),
            ])
        else:
            reply = b"".join([push(751), push(reply_ids[0]), opcode(0x811E)])
        encoded_options = []
        for message_id, target_index, intelligence, maximum in options:
            encoded_options.extend([
                push(intelligence),
                opcode(0x8046) if maximum else b"",
                push(751), push(message_id), push(target_index), push(50),
                opcode(0x8121),
            ])
        return b"".join([opcode(0x802B), reply, *encoded_options, epilogue])

    bodies_by_name["Node998"] = b"".join([
        opcode(0x802B), push(5), push(2), opcode(0x80C2), epilogue,
    ])
    bodies_by_name["Node999"] = opcode(0x802B) + epilogue
    bodies_by_name["Node001"] = dialogue_node(
        (103, 104),
        [(105, 15, 3, True), (106, 16, 4, False),
         (107, 17, 4, False), (108, 13, 4, False)],
    )
    bodies_by_name["Node002"] = dialogue_node((109, 110), [(111, 13, 3, True)])
    bodies_by_name["Node003"] = dialogue_node(
        (112, 113),
        [(114, 13, 4, False), (115, 17, 4, False), (116, 18, 4, False)],
    )
    bodies_by_name["Node004"] = dialogue_node((117,), [(118, 13, 4, False)])
    bodies_by_name["Node005"] = dialogue_node((119,), [(120, 13, 4, False)])

    bodies = []
    body_offset = body_start
    payload = bytearray()
    for name in names:
        bodies.append(body_offset)
        body = bodies_by_name[name]
        payload.extend(body)
        body_offset += len(body)
    table = b"".join(
        struct.pack(">6I", name_offsets[index], 0, 0, 0, bodies[index], 0)
        for index in range(len(names))
    )
    return (
        bytes(42)
        + struct.pack(">I", len(names))
        + table
        + struct.pack(">I", len(identifiers))
        + identifiers
        + payload
    )


def synthetic_confrontation_map() -> bytes:
    data = synthetic_map()
    header_and_tiles_and_scripts = data[: 0xEC + 10000 * 4 + 20]
    critter_base = struct.pack(
        ">21i",
        1,
        21101,
        0,
        0,
        0,
        0,
        0,
        5,
        0x01004040,
        0x20000000,
        0,
        0x01000003,
        -1,
        0,
        0,
        0,
        0x04000001,
        750,
        1,
        10,
        0,
    )
    critter_instance = struct.pack(">11i", 0, 0, 0, 9, 0, 1, 1, -1, 50, 0, 0)
    weapon_base = struct.pack(
        ">21i",
        2,
        -1,
        0,
        0,
        0,
        0,
        0,
        0,
        0x0000002A,
        0x02000008,
        0,
        0x00000007,
        -1,
        0,
        0,
        0,
        -1,
        -1,
        0,
        0,
        0,
    )
    weapon_instance = struct.pack(">3i", 0, 0, -1)
    objects = (
        struct.pack(">2i", 1, 1)
        + critter_base
        + critter_instance
        + struct.pack(">i", 1)
        + weapon_base
        + weapon_instance
        + struct.pack(">2i", 0, 0)
    )
    return header_and_tiles_and_scripts + objects


class Fo2FirstSliceTest(unittest.TestCase):
    def test_compiles_exact_map_object_pro_and_frm_graph_without_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            install = root / "Fallout 2"
            install.mkdir()
            map_data = synthetic_confrontation_map()
            critter_pro = synthetic_critter_pro()
            weapon_pro = synthetic_weapon_pro()
            critter_list = b"unused.pro\r\nunused.pro\r\n00000003.pro\r\n"
            item_list = b"unused.pro\r\n" * 6 + b"00000013.pro\r\n"
            critter_art_list = b"unused.frm\r\n" * 64 + b"nmwarr,11,1\r\n"
            item_art_list = b"unused.frm\r\n" * 42 + b"spear.frm\r\n"
            guardian_script = synthetic_acklint_int()
            guardian_messages = b"".join(
                f"{{{message_id}}}{{}}{{guardian {message_id}}}\r\n".encode("ascii")
                for message_id in range(100, 121)
            )
            script_entries = ["unused.int"] * 751
            script_entries[750] = "ACKlint.int"
            scripts_list = ("\r\n".join(script_entries) + "\r\n").encode("ascii")
            (install / "master.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("maps\\artemple.map", map_data, True),
                        ("proto\\critters\\critters.lst", critter_list, False),
                        ("proto\\critters\\00000003.pro", critter_pro, False),
                        ("proto\\items\\items.lst", item_list, False),
                        ("proto\\items\\00000013.pro", weapon_pro, False),
                        ("art\\critters\\critters.lst", critter_art_list, False),
                        ("art\\critters\\nmwarrga.frm", synthetic_frm(), True),
                        ("art\\items\\items.lst", item_art_list, False),
                        ("art\\items\\spear.frm", synthetic_frm(), True),
                        ("text\\english\\game\\pro_crit.msg", b"{300}{}{Villager}\r\n", False),
                        ("text\\english\\game\\pro_item.msg", b"{700}{}{Spear}\r\n", False),
                        ("text\\english\\dialog\\acklint.msg", guardian_messages, False),
                        ("scripts\\acklint.int", guardian_script, False),
                        ("scripts\\scripts.lst", scripts_list, False),
                    ]
                )
            )
            (install / "critter.dat").write_bytes(
                synthetic_dat2([("art\\critters\\unused.frm", b"unused", False)])
            )
            (install / "patch000.dat").write_bytes(
                synthetic_dat2(
                    [
                        (
                            "data\\maps.txt",
                            b"[Map 126]\r\nlookup_name=Arroyo Temple\r\nmap_name=artemple\r\n",
                            True,
                        )
                    ]
                )
            )

            profile_path = root / "fallout2-profile.json"
            profile = inspect_fo2_profile(install, "synthetic")
            profile_path.write_text(json.dumps(profile), encoding="utf-8")
            recipe_path = root / "synthetic-temple.json"
            recipe_path.write_text(
                json.dumps(
                    {
                        "schema": "opennv-fo2-first-slice-recipe/v2",
                        "id": recipe_path.stem,
                        "campaign": "Fallout2",
                        "sourceProfileSchema": "opennv-fo2-owned-profile/v1",
                        "overlayOrderHighToLow": [
                            "patch000.dat",
                            "critter.dat",
                            "master.dat",
                        ],
                        "mapRegistry": {
                            "logicalPath": "data\\maps.txt",
                            "section": "Map 126",
                            "lookupName": "Arroyo Temple",
                            "mapName": "artemple",
                        },
                        "map": {
                            "logicalPath": "maps\\artemple.map",
                            "sha256": hashlib.sha256(map_data).hexdigest(),
                            "header": {
                                "version": 20,
                                "name": "ARTEMPLE.MAP",
                                "enteringTile": 18492,
                                "enteringElevation": 0,
                                "enteringRotation": 0,
                                "localVariables": 0,
                                "scriptIndex": 745,
                                "flags": 12,
                                "darkness": 1,
                                "globalVariables": 0,
                                "mapIndex": 126,
                                "lastVisitTime": 0,
                            },
                            "presentElevations": [0],
                        },
                        "boundedConfrontation": {
                            "schema": "opennv-fo2-temple-confrontation-recipe/v1",
                            "critter": {
                                "serial": 2,
                                "tile": 21101,
                                "pid": "01000003",
                                "sid": "04000001",
                                "prototypeSha256": hashlib.sha256(critter_pro).hexdigest(),
                            },
                            "loot": {
                                "serial": 1,
                                "pid": "00000007",
                                "quantity": 1,
                                "prototypeSha256": hashlib.sha256(weapon_pro).hexdigest(),
                            },
                            "messageCatalogs": {
                                "critter": "text\\english\\game\\pro_crit.msg",
                                "item": "text\\english\\game\\pro_item.msg",
                            },
                            "guardianScript": {
                                "program": {
                                    "scriptsListIndex": 750,
                                    "logicalPath": "scripts\\acklint.int",
                                    "sha256": hashlib.sha256(guardian_script).hexdigest(),
                                },
                                "messageCatalog": {
                                    "logicalPath": "text\\english\\dialog\\acklint.msg",
                                    "sha256": hashlib.sha256(guardian_messages).hexdigest(),
                                    "messageListId": 751,
                                },
                                "preTrialPlayerArtFids": ["0100003d", "0100003e"],
                                "initialNode": "Node001",
                                "terminalNode": "Node999",
                                "nodes": [
                                    {
                                        "id": "Node001",
                                        "reply": [
                                            {"messageId": 103},
                                            {"playerName": True},
                                            {"messageId": 104},
                                        ],
                                        "options": [
                                            {"messageId": 105, "target": "Node002", "maximumIntelligence": 3, "reaction": 50},
                                            {"messageId": 106, "target": "Node003", "minimumIntelligence": 4, "reaction": 50},
                                            {"messageId": 107, "target": "Node004", "minimumIntelligence": 4, "reaction": 50},
                                            {"messageId": 108, "target": "Node999", "minimumIntelligence": 4, "reaction": 50},
                                        ],
                                    },
                                    {
                                        "id": "Node002",
                                        "reply": [
                                            {"messageId": 109},
                                            {"playerName": True},
                                            {"messageId": 110},
                                        ],
                                        "options": [
                                            {"messageId": 111, "target": "Node999", "maximumIntelligence": 3, "reaction": 50},
                                        ],
                                    },
                                    {
                                        "id": "Node003",
                                        "reply": [
                                            {"messageId": 112},
                                            {"playerName": True},
                                            {"messageId": 113},
                                        ],
                                        "options": [
                                            {"messageId": 114, "target": "Node999", "minimumIntelligence": 4, "reaction": 50},
                                            {"messageId": 115, "target": "Node004", "minimumIntelligence": 4, "reaction": 50},
                                            {"messageId": 116, "target": "Node005", "minimumIntelligence": 4, "reaction": 50},
                                        ],
                                    },
                                    {
                                        "id": "Node004",
                                        "reply": [{"messageId": 117}],
                                        "options": [
                                            {"messageId": 118, "target": "Node999", "minimumIntelligence": 4, "reaction": 50},
                                        ],
                                    },
                                    {
                                        "id": "Node005",
                                        "reply": [{"messageId": 119}],
                                        "options": [
                                            {"messageId": 120, "target": "Node999", "minimumIntelligence": 4, "reaction": 50},
                                        ],
                                    },
                                ],
                            },
                        },
                        "declaredRole": "synthetic Temple source slice",
                        "unsupported": ["runtime"],
                    }
                ),
                encoding="utf-8",
            )

            document = compile_fo2_first_slice(profile_path, recipe_path)

            self.assertEqual(document["status"], "transported-source-manifest")
            self.assertEqual(document["newGameStart"]["playerEntry"]["tile"], 18492)
            self.assertFalse(document["newGameStart"]["playerEntry"]["placedPlayerObject"])
            self.assertEqual(document["map"]["objects"]["totalTopLevelObjects"], 1)
            self.assertEqual(document["map"]["allObjectCount"], 2)
            confrontation = document["boundedConfrontation"]
            self.assertEqual(confrontation["critter"]["serial"], 2)
            self.assertEqual(confrontation["critter"]["currentHitPoints"], 50)
            self.assertEqual(confrontation["critter"]["prototype"]["stats"]["actionPoints"], 9)
            self.assertEqual(confrontation["defeatLoot"]["serial"], 1)
            self.assertEqual(confrontation["defeatLoot"]["displayName"], "Spear")
            self.assertEqual(
                confrontation["defeatLoot"]["prototype"]["weapon"]["actionPointCostPrimary"],
                4,
            )
            guardian = confrontation["guardianScript"]
            self.assertEqual(guardian["program"]["scriptsListIndex"], 750)
            self.assertEqual(guardian["program"]["sha256"], hashlib.sha256(guardian_script).hexdigest())
            self.assertEqual(guardian["messageCatalog"]["messageListId"], 751)
            self.assertEqual([row["id"] for row in guardian["nodes"]], ["Node001", "Node002", "Node003", "Node004", "Node005"])
            self.assertEqual(guardian["nodes"][2]["options"][2]["messageId"], 116)
            self.assertEqual(guardian["nodes"][2]["options"][2]["target"], "Node005")
            self.assertTrue(guardian["implementedBoundary"]["dialogueNodes"])
            self.assertTrue(guardian["implementedBoundary"]["pickupToAttackTransition"])
            self.assertFalse(guardian["implementedBoundary"]["generalIntExecution"])
            effects = guardian["effectProgram"]
            self.assertEqual(effects["schema"], "opennv-classic-script-effects/v1")
            self.assertEqual(
                effects["events"]["pickup_proc"][0]["then"][0],
                {"operation": "set-local", "index": 5, "value": 2},
            )
            self.assertEqual(
                [row["messageId"] for row in guardian["displayMessages"]],
                [100, 101],
            )
            self.assertEqual(
                effects["events"]["look_at_p_proc"][0]["then"][2]["messageId"],
                100,
            )
            self.assertTrue(document["promotion"]["transported"])
            self.assertFalse(document["runtimeCompatibility"]["ready"])
            self.assertFalse(document["retailOrDerivedAssetsPackaged"])
            self.assertEqual(document["generatedCaches"], [])


if __name__ == "__main__":
    unittest.main()
