from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_catalog import resolve_actor_outfit_form_ids, scan_actor_catalog  # noqa: E402
from prepare_actor import resolve_proof_creature  # noqa: E402


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes, flags: int = 0) -> bytes:
    return struct.pack("<4s4I2H", signature.encode("ascii"), len(data), flags, form_id, 0, 0, 0) + data


def group(label: bytes, group_type: int, contents: bytes) -> bytes:
    return struct.pack("<4sI4siHHI", b"GRUP", 24 + len(contents), label, group_type, 0, 0, 0) + contents


def plugin() -> bytes:
    header = record("TES4", 0, subrecord("HEDR", struct.pack("<fII", 1.34, 5, 0)))
    race = record(
        "RACE",
        0x19,
        subrecord("EDID", b"SyntheticRace\0")
        + subrecord("NAM0", b"")
        + subrecord("MNAM", b"")
        + subrecord("INDX", struct.pack("<I", 0))
        + subrecord("MODL", b"characters/head/headmale.nif\0")
        + subrecord("FNAM", b"")
        + subrecord("INDX", struct.pack("<I", 0))
        + subrecord("MODL", b"characters/head/headfemale.nif\0")
        + subrecord("ICON", b"characters/female/head.dds\0")
        + subrecord("INDX", struct.pack("<I", 6))
        + subrecord("MODL", b"characters/head/eyeleftfemale.nif\0")
        + subrecord("NAM1", b"")
        + subrecord("FNAM", b"")
        + subrecord("INDX", struct.pack("<I", 0))
        + subrecord("MODL", b"characters/_male/femaleupperbody.nif\0")
        + subrecord("ICON", b"characters/female/upperbody.dds\0")
        + subrecord("FGGS", struct.pack("<50f", *([1.0] * 50)))
        + subrecord("FGGA", struct.pack("<30f", *([2.0] * 30)))
        + subrecord("FGTS", struct.pack("<50f", *([3.0] * 50))),
    )
    hair = record(
        "HAIR",
        0x30,
        subrecord("EDID", b"SyntheticHair\0") + subrecord("MODL", b"characters/hair/test.nif\0"),
    )
    eyes = record(
        "EYES",
        0x31,
        subrecord("EDID", b"SyntheticEyes\0") + subrecord("ICON", b"characters/eyes/green.dds\0"),
    )
    eyebrow = record(
        "HDPT",
        0x32,
        subrecord("EDID", b"SyntheticBrow\0") + subrecord("MODL", b"characters/hair/brow.nif\0"),
    )
    armor = record(
        "ARMO",
        0x40,
        subrecord("EDID", b"SyntheticOutfit\0")
        + subrecord("FULL", b"Outfit\0")
        + subrecord("MODL", b"armor/male.nif\0")
        + subrecord("MOD2", b"armor/male_go.nif\0")
        + subrecord("MOD3", b"armor/female.nif\0")
        + subrecord("MOD4", b"armor/female_go.nif\0")
        + subrecord("BMDT", struct.pack("<II", 0x00000004, 0)),
    )
    outfit_list = record(
        "LVLI",
        0x41,
        subrecord("EDID", b"SyntheticOutfitList\0")
        + subrecord("LVLO", struct.pack("<HHIHH", 1, 0, 0x40, 1, 0))
        + subrecord("LVLO", struct.pack("<HHIHH", 1, 0, 0x40, 1, 0)),
    )
    actor = record(
        "NPC_",
        0x50,
        subrecord("EDID", b"SyntheticActor\0")
        + subrecord("FULL", b"Actor\0")
        + subrecord("MODL", b"characters/_male/skeleton.nif\0")
        + subrecord("ACBS", struct.pack("<6I", 1, 0, 0, 0, 0, 0))
        + subrecord("RNAM", struct.pack("<I", 0x19))
        + subrecord("HNAM", struct.pack("<I", 0x30))
        + subrecord("ENAM", struct.pack("<I", 0x31))
        + subrecord("PNAM", struct.pack("<I", 0x32))
        + subrecord("LNAM", struct.pack("<f", 0.25))
        + subrecord("HCLR", bytes((28, 4, 2, 0)))
        + subrecord("CNTO", struct.pack("<Ii", 0x41, 1))
        + subrecord("EAMT", struct.pack("<H", 0x0101))
        + subrecord("FGGS", struct.pack("<50f", *range(50)))
        + subrecord("FGGA", struct.pack("<30f", *range(30)))
        + subrecord("FGTS", struct.pack("<50f", *range(50))),
    )
    actor_reference = record(
        "ACHR",
        0x60,
        subrecord("NAME", struct.pack("<I", 0x50))
        + subrecord("XSCL", struct.pack("<f", 0.95))
        + subrecord("DATA", struct.pack("<6f", 1.0, 2.0, 3.0, 0.0, 0.0, 1.5)),
        0x00000800,
    )
    cell = record("CELL", 0x100, subrecord("EDID", b"SyntheticCell\0"))
    creature = record(
        "CREA",
        0x70,
        subrecord("EDID", b"SyntheticCreature\0")
        + subrecord("FULL", b"Creature\0")
        + subrecord("MODL", b"creatures/test/skeleton.nif\0")
        + subrecord("NIFZ", b"test-base.nif\0test-extra.nif\0\0")
        + subrecord("ACBS", struct.pack("<6I", 0x20, 0, 0, 0, 0, 0))
        + subrecord("EAMT", struct.pack("<H", 0x0040)),
    )
    creature_reference = record(
        "ACRE",
        0x71,
        subrecord("NAME", struct.pack("<I", 0x70))
        + subrecord("XESP", struct.pack("<II", 0x72, 1))
        + subrecord("DATA", struct.pack("<6f", 4.0, 5.0, 6.0, 0.0, 0.0, 0.0)),
    )
    children = group(
        struct.pack("<I", 0x100),
        6,
        group(struct.pack("<I", 0x100), 9, actor_reference + creature_reference),
    )
    return (
        header
        + group(b"RACE", 0, race)
        + group(b"HAIR", 0, hair)
        + group(b"EYES", 0, eyes)
        + group(b"HDPT", 0, eyebrow)
        + group(b"ARMO", 0, armor)
        + group(b"LVLI", 0, outfit_list)
        + group(b"NPC_", 0, actor)
        + group(b"CREA", 0, creature)
        + group(b"CELL", 0, cell + children)
    )


class ActorCatalogTest(unittest.TestCase):
    def test_actor_graph_retains_identity_appearance_and_placement(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.esm"
            path.write_bytes(plugin())
            catalog = scan_actor_catalog(path)

        actor = catalog.actors[0x50]
        self.assertTrue(actor.female)
        self.assertEqual(actor.actor_flags, 1)
        self.assertEqual(actor.template_flags, 0x0101)
        self.assertEqual(actor.skeleton_path, "characters\\_male\\skeleton.nif")
        self.assertEqual(actor.race_form_id, 0x19)
        self.assertEqual(actor.hair_form_id, 0x30)
        self.assertEqual(actor.eyes_form_id, 0x31)
        self.assertEqual(actor.head_part_form_ids, (0x32,))
        self.assertEqual(actor.inventory[0].form_id, 0x41)
        self.assertEqual(len(actor.face_symmetric_geometry), 50)
        self.assertEqual(len(actor.face_asymmetric_geometry), 30)
        self.assertEqual(len(actor.face_symmetric_texture), 50)
        race = catalog.races[0x19]
        self.assertEqual(race.female_head_models[0], "characters\\head\\headfemale.nif")
        self.assertEqual(race.female_head_models[6], "characters\\head\\eyeleftfemale.nif")
        self.assertEqual(race.female_head_textures[0], "characters\\female\\head.dds")
        self.assertEqual(race.female_body_models[0], "characters\\_male\\femaleupperbody.nif")
        self.assertEqual(race.female_body_textures[0], "characters\\female\\upperbody.dds")
        self.assertEqual(race.female_face_symmetric_geometry[0], 1.0)
        self.assertEqual(race.female_face_asymmetric_geometry[0], 2.0)
        self.assertEqual(race.female_face_symmetric_texture[0], 3.0)
        self.assertEqual(catalog.armor[0x40].female_model_path, "armor\\female.nif")
        self.assertEqual(catalog.armor[0x40].female_ground_model_path, "armor\\female_go.nif")
        self.assertEqual(catalog.armor[0x40].biped_flags, 0x00000004)
        self.assertFalse(catalog.armor[0x40].hides_hair)
        self.assertEqual(resolve_actor_outfit_form_ids(catalog, actor), (0x40,))
        references = catalog.references_for(0x100)
        reference = references[0]
        self.assertEqual(reference.actor_form_id, actor.form_id)
        self.assertEqual(reference.record_type, "ACHR")
        self.assertEqual(reference.position, (1.0, 2.0, 3.0))
        self.assertAlmostEqual(reference.scale, 0.95)
        self.assertTrue(reference.initially_disabled)
        self.assertIsNone(reference.enable_parent_form_id)
        self.assertEqual(references[1].record_type, "ACRE")
        self.assertEqual(references[1].enable_parent_form_id, 0x72)
        self.assertEqual(catalog.creatures[0x70].name, "Creature")
        self.assertEqual(catalog.creatures[0x70].actor_flags, 0x20)
        self.assertEqual(catalog.creatures[0x70].template_flags, 0x0040)
        self.assertEqual(
            catalog.creatures[0x70].model_paths,
            ("test-base.nif", "test-extra.nif"),
        )
        creature_reference, creature = resolve_proof_creature(
            catalog,
            0x71,
            0x100,
            0x70,
        )
        self.assertEqual(creature_reference.actor_form_id, creature.form_id)


if __name__ == "__main__":
    unittest.main()
