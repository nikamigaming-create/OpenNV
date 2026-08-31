from __future__ import annotations

import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from classic_int_effects import ClassicIntDecodeError, decode_acklint_effects  # noqa: E402
from classic_ssl_effects import (  # noqa: E402
    ClassicSslParseError,
    decode_flare_effects,
    decode_single_message_look,
)
from content.tests.test_fo2_first_slice import synthetic_acklint_int  # noqa: E402


FLARE_SSL = """
procedure start;
variable lit;
procedure start
begin
  if (script_action == use_proc) then begin
    if not(local_var(3)) then begin
      set_local_var(3, game_time);
    end
    if ((lit != 1) and (lit != 2)) then begin
      lit := 1;
    end
  end
  if (script_action == start_proc) then begin
    if (((game_time - local_var(3)) > (2 * 60 * 60 * 10))) then begin
      destroy_object(self_obj);
    end
  end
end
"""


class ClassicScriptDecoderTest(unittest.TestCase):
    def test_int_decoder_recovers_acklint_operands_from_procedures(self) -> None:
        program = decode_acklint_effects(synthetic_acklint_int())
        pickup = program["events"]["pickup_proc"][0]
        critter = program["events"]["critter_proc"][0]
        self.assertEqual(
            pickup["then"],
            [{"operation": "set-local", "index": 5, "value": 2}],
        )
        self.assertEqual(critter["all"][0]["index"], 5)
        self.assertEqual(critter["then"][0]["value"], 1)
        self.assertEqual(critter["then"][1]["flag"], "attack-player-requested")
        look = program["events"]["look_at_p_proc"]
        self.assertEqual(look[0]["all"][0]["index"], 7)
        self.assertEqual(look[0]["then"][2]["messageId"], 100)
        self.assertEqual(look[1]["then"][1]["messageId"], 101)

    def test_int_decoder_rejects_opcode_and_branch_drift(self) -> None:
        source = synthetic_acklint_int()
        opcode_drift = bytearray(source)
        location = opcode_drift.find(bytes.fromhex("80 dc"))
        self.assertGreater(location, 0)
        opcode_drift[location:location + 2] = bytes.fromhex("80 00")
        with self.assertRaises(ClassicIntDecodeError):
            decode_acklint_effects(bytes(opcode_drift))

        branch_drift = bytearray(source)
        location = branch_drift.find(bytes.fromhex("80 2b c0 01"))
        self.assertGreater(location, 0)
        branch_drift[location + 4:location + 8] = bytes(4)
        with self.assertRaises(ClassicIntDecodeError):
            decode_acklint_effects(bytes(branch_drift))

    def test_ssl_parser_recovers_flare_local_and_expiry(self) -> None:
        program, expiry = decode_flare_effects(FLARE_SSL)
        effect = program["events"]["use_proc"][0]["then"][0]
        self.assertEqual(
            program["events"]["use_proc"][0]["all"],
            [{"operation": "local-equals", "index": 3, "value": 0}],
        )
        self.assertEqual(effect["index"], 3)
        self.assertEqual(effect["valueFrom"], "game-time")
        self.assertEqual(program["events"]["use_proc"][1]["all"], [])
        self.assertEqual(expiry["localIndex"], 3)
        self.assertEqual(expiry["durationGameTicks"], 72000)
        self.assertEqual(expiry["runtime"], "unimplemented-fail-closed")

    def test_ssl_parser_rejects_non_source_duration_or_effect(self) -> None:
        with self.assertRaises(ClassicSslParseError):
            decode_flare_effects(FLARE_SSL.replace("2 * 60 * 60 * 10", "42"))
        with self.assertRaises(ClassicSslParseError):
            decode_flare_effects(FLARE_SSL.replace("lit := 1", "lit := 2"))
        with self.assertRaises(ClassicSslParseError):
            decode_flare_effects(FLARE_SSL.replace("destroy_object", "display_msg"))

    def test_ssl_parser_recovers_single_message_look_actions(self) -> None:
        source = """
        procedure look_at_p_proc begin
          script_overrides;
          display_msg(mstr(136));
        end
        """
        program = decode_single_message_look(source)
        effects = program["events"]["look_at_p_proc"][0]["then"]
        self.assertEqual(effects[0]["operation"], "script-overrides")
        self.assertEqual(effects[1]["messageId"], 136)
        with self.assertRaises(ClassicSslParseError):
            decode_single_message_look(source.replace("script_overrides;", ""))


if __name__ == "__main__":
    unittest.main()
