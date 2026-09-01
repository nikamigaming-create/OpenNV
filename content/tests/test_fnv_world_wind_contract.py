from __future__ import annotations

import sys
import unittest
from pathlib import Path
from types import SimpleNamespace


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fnv_world_wind_contract import (  # noqa: E402
    HAVOK_LISTENER_TYPE,
    MAXIMUM_WIND_SETTING,
    REQUIRED_EXECUTABLE_IDENTITIES,
    WEATHER_LISTENER_TYPE,
    extract_world_wind_denominator,
)


class FnvWorldWindContractTest(unittest.TestCase):
    def test_extracts_owned_weather_denominator_without_force_equation(self) -> None:
        executable = b"prefix" + b"|".join(REQUIRED_EXECUTABLE_IDENTITIES) + b"suffix"
        catalog = SimpleNamespace(weather={
            0x20: SimpleNamespace(editor_id="SecondWeather", wind_speed_byte=90),
            0x10: SimpleNamespace(editor_id="FirstWeather", wind_speed_byte=30),
        })

        contract = extract_world_wind_denominator(executable, catalog)

        self.assertEqual(
            contract["status"],
            "source-denominator-force-equation-unresolved",
        )
        self.assertEqual(
            [row["formId"] for row in contract["weather"]],
            ["0x00000010", "0x00000020"],
        )
        self.assertEqual(
            [row["magnitudeByte"] for row in contract["weather"]],
            [30, 90],
        )
        self.assertIsNone(contract["maximumWind"]["value"])
        self.assertEqual(contract["runtimeDisposition"], "do-not-apply-world-force")

    def test_fails_closed_when_executable_identity_is_ambiguous_or_absent(self) -> None:
        catalog = SimpleNamespace(weather={
            0x10: SimpleNamespace(editor_id="Weather", wind_speed_byte=30),
        })
        for executable in (
            MAXIMUM_WIND_SETTING + WEATHER_LISTENER_TYPE,
            b"".join(REQUIRED_EXECUTABLE_IDENTITIES) + HAVOK_LISTENER_TYPE,
        ):
            with self.subTest(executable=executable):
                with self.assertRaisesRegex(ValueError, "one exact FNV wind identity"):
                    extract_world_wind_denominator(executable, catalog)


if __name__ == "__main__":
    unittest.main()
