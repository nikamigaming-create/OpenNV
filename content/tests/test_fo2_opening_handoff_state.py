from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RECIPE = ROOT / "content" / "recipes" / "fo2-character-start-v2.json"
PRODUCER = ROOT / "content" / "tools" / "prepare_fo2_character_start.py"
RUNTIME = (
    ROOT
    / "runtime"
    / "src"
    / "Campaigns"
    / "Fallout2"
    / "CharacterStart"
)


class Fo2OpeningHandoffStateTest(unittest.TestCase):
    def test_owned_movie_and_cfg_define_the_terminal_fade_boundary(self) -> None:
        opening = json.loads(RECIPE.read_text(encoding="utf-8"))["openingTail"]

        self.assertEqual(opening["movie"]["logicalPath"], "art\\cuts\\elder.mve")
        self.assertEqual(
            opening["movie"]["sha256"],
            "bb8118d90606907c543ec58f543b7087b431771eb0aa2858d1f9006a48ca4ef8",
        )
        self.assertEqual(
            opening["fadeConfig"]["logicalPath"], "art\\cuts\\elder.cfg"
        )
        self.assertEqual(
            opening["fadeConfig"]["sha256"],
            "23e923c77cbd25602f78fb6de2515c8f086403291b612d0b43c4d7c13002ae16",
        )
        video = opening["video"]
        fade = opening["fade"]
        self.assertEqual(video["playbackStartFrame"], 1)
        self.assertEqual(video["tailStartFrame"], fade["startFrame"])
        self.assertEqual(video["sourceFrameCount"], 1145)
        self.assertEqual(fade["startFrame"], 1118)
        self.assertEqual(fade["steps"], 30)
        self.assertEqual(fade["startFrame"] + fade["steps"] - 1, 1147)
        self.assertEqual(video["sourceFrameCount"] - fade["startFrame"] + 1, 28)
        self.assertTrue(fade["movieEndForcesBlack"])

        producer = PRODUCER.read_text(encoding="utf-8")
        self.assertIn('"terminalFrame": source_frame_count', producer)
        self.assertIn('"playbackStartFrame": playback_start_frame', producer)
        self.assertIn('"playbackFrameCount": playback_frame_count', producer)
        self.assertIn('opening_frames.append(', producer)
        self.assertIn('"-count_frames"', producer)
        self.assertIn("_parse_opening_fade_config(", producer)

    def test_runtime_streams_every_owned_frame_without_preloading_the_movie(self) -> None:
        contract = (RUNTIME / "Fo2OpeningTailContract.cs").read_text(encoding="utf-8")
        handoff = (RUNTIME / "Fo2OpeningTailHandoff.cs").read_text(encoding="utf-8")
        proof = (RUNTIME / "Fo2OpeningHandoffProof.cs").read_text(encoding="utf-8")

        self.assertIn("playbackStart != 1", contract)
        self.assertIn("sourceFrames - playbackStart + 1", contract)
        self.assertIn("sourceFrame != playbackStart + index", contract)
        self.assertIn("if (sourceFrame < FadeStartFrame)\n            return 0.0f;", contract)
        self.assertIn("Image.LoadFromFile(source.Path)", handoff)
        self.assertNotIn("List<ImageTexture>", handoff)
        self.assertIn("previous?.Dispose();", handoff)
        self.assertIn("var playbackClock = Stopwatch.StartNew();", handoff)
        self.assertIn("index * contract.FramePeriodSeconds", handoff)
        self.assertIn("contract.Frames.Count * contract.FramePeriodSeconds", handoff)
        self.assertIn("contract.PlaybackStartFrame", proof)

    def test_skip_converges_through_terminal_source_state_and_shared_release(self) -> None:
        contract = (RUNTIME / "Fo2OpeningTailContract.cs").read_text(encoding="utf-8")
        handoff = (RUNTIME / "Fo2OpeningTailHandoff.cs").read_text(encoding="utf-8")
        arrival = (RUNTIME / "Fo2ArrivalVisualProof.cs").read_text(encoding="utf-8")
        uninterrupted = (RUNTIME / "Fo2OpeningHandoffProof.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn(
            "internal int TerminalFadeStep => TerminalFrame - FadeStartFrame + 1;",
            contract,
        )
        self.assertIn("terminalFrame > fadeEnd", contract)
        self.assertIn("contract.SourceFadeFraction(source.SourceFrame)", handoff)
        self.assertIn(
            "var terminalIndex = contract.Frames.Count - 1;",
            handoff,
        )
        self.assertIn("SkipTerminalStateApplied = true;", handoff)
        terminal_state = handoff.index(
            "var terminalIndex = contract.Frames.Count - 1;"
        )
        movie_end = handoff.index("audio.Stop();")
        full_black = handoff.index("fade.Color = Colors.Black;")
        reveal = handoff.index("scene.Root.Visible = true;")
        release = handoff.index("runtime.Player.SetControlsEnabled(true);")
        self.assertLess(terminal_state, movie_end)
        self.assertLess(movie_end, full_black)
        self.assertLess(full_black, reveal)
        self.assertLess(reveal, release)

        self.assertIn("!handoff.SkipTerminalStateApplied", arrival)
        self.assertIn("contract.TerminalFadeFraction", arrival)
        self.assertIn("handoff.FinalPresentedSourceFrame != contract.TerminalFrame", arrival)
        self.assertIn("!handoff.SkipRequested", uninterrupted)
        self.assertIn("!handoff.SkipTerminalStateApplied", uninterrupted)


if __name__ == "__main__":
    unittest.main()
