from __future__ import annotations

import tempfile
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_fo3_profile import (  # noqa: E402
    _appearance_ui_contract,
    _compile_fo3_ui_fonts,
)


class Fo3OwnedUiTileContractTest(unittest.TestCase):
    def test_dialogue_and_shared_creator_font_ids_compile_once(self) -> None:
        dialogue = {
            "speakerName": {"font": 7},
            "speakerText": {"font": 6},
            "topics": {"template": {"font": 6}},
        }
        appearance = {"ui": {"raceSexMenuTiles": {"fontId": 7}}}
        compiled = []

        def compile_font(font_id, *_args):
            compiled.append(font_id)
            return ({"schema": "opennv-owned-gamebryo-bitmap-font/v1"}, {})

        with tempfile.TemporaryDirectory() as temporary, patch(
            "prepare_fo3_profile._compile_gamebryo_font",
            side_effect=compile_font,
        ):
            result = _compile_fo3_ui_fonts(
                dialogue,
                appearance,
                {},
                {"section": "Fonts", "keyTemplate": "sFontFile_{id}"},
                object(),
                Path(temporary),
                object(),
            )

        self.assertEqual(compiled, [6, 7])
        self.assertEqual([row["fontId"] for row in result], [6, 7])

    def test_name_panel_and_prompt_remain_owned_xml_entity_bound(self) -> None:
        appearance_xml = br"""
            <menu name="RaceSexMenu"><x>930</x><y>550</y>
            <rect name="NOGLOW_BRANCH"><width>340</width><height>500</height>
            <image name="RSM_Background"></image></rect>
            <template name="RSM_list_item_template"><width>320</width><height>36</height></template>
            <template name="RSM_slider_option_template"><width>320</width><height>64</height></template>
            <hotrect name="RSM_Face_Grab"><x>150</x><y>50</y><width>680</width><height>620</height></hotrect>
            Interface\Shared\Background\pipboy.dds</menu>
        """
        name_xml = br"""
            <menu name="TextEditMenu"><rect name="TEM_MainRect">
            <width>720</width><height>180</height>
            <x><copy src="screen()" trait="width"/><sub src="me()" trait="width"/><div>2</div></x>
            <y><copy src="screen()" trait="height"/><sub src="me()" trait="height"/><div>2</div></y>
            <text name="textedit_prompt"><string>&-sEnterName;</string><justify>&center;</justify>
            <x><copy src="parent()" trait="width"/><div>2</div></x><y>32</y></text>
            <text name="textedit_text"><justify>&center;</justify><wrapwidth>250</wrapwidth>
            <x><copy src="parent()" trait="width"/><div>2</div></x>
            <y><copy src="parent()" trait="height"/><sub src="me()" trait="height"/><div>2</div></y></text>
            <hotrect name="textedit_button_ok"><string>&-sOk;</string><justify>&right;</justify>
            <_x><copy src="parent()" trait="width"/><sub>16</sub></_x>
            <_y><copy src="parent()" trait="height"/><sub src="me()" trait="height"/><sub>16</sub></_y></hotrect>
            Interface\Shared\Background\solid_black.dds</rect></menu>
        """
        definition = {
            "sourceCanvasWidth": 1600,
            "sourceCanvasHeight": 1200,
            "document": "menus\\chargen\\race_sex_menu.xml",
            "menuName": "RaceSexMenu",
            "panelName": "NOGLOW_BRANCH",
            "panelX": 930,
            "panelY": 550,
            "panelWidth": 340,
            "panelHeight": 500,
            "faceGrabX": 150,
            "faceGrabY": 50,
            "faceGrabWidth": 680,
            "faceGrabHeight": 620,
            "listItemWidth": 320,
            "listItemHeight": 36,
            "sliderWidth": 320,
            "sliderHeight": 64,
            "backgroundTexture": "textures\\interface\\shared\\background\\pipboy.dds",
            "nameDocument": "menus\\dialog\\texteditmenu.xml",
            "nameMenuName": "TextEditMenu",
            "namePanelName": "TEM_MainRect",
            "namePromptTile": "textedit_prompt",
            "namePromptEntity": "-sEnterName",
            "nameInputTile": "textedit_text",
            "nameAcceptTile": "textedit_button_ok",
            "nameAcceptEntity": "-sOk",
            "appearanceBackEntity": "-sBack",
            "appearanceNextEntity": "-sNext",
            "namePanelWidth": 720,
            "namePanelHeight": 180,
            "nameBackgroundTexture": "textures\\interface\\shared\\background\\solid_black.dds",
        }
        members = {
            "menus\\chargen\\race_sex_menu.xml": SimpleNamespace(
                data=appearance_xml, sha256="a" * 64
            ),
            "menus\\dialog\\texteditmenu.xml": SimpleNamespace(
                data=name_xml, sha256="b" * 64
            ),
            "menus\\prefabs\\text_box.xml": SimpleNamespace(
                data=b"<rect></rect>", sha256="c" * 64
            ),
            "menus\\levelup_menu.xml": SimpleNamespace(
                data=b"<string>&-sBack;</string>", sha256="e" * 64
            ),
            "menus\\tutorial_menu.xml": SimpleNamespace(
                data=b"<string>&-sNext;</string>", sha256="f" * 64
            ),
        }
        race_sex_tiles = {
            "navigation": {
                "back": {"tile": "RSM_back_button"},
                "next": {"tile": "RSM_next_button"},
            }
        }
        with tempfile.TemporaryDirectory() as temporary, patch(
            "prepare_fo3_profile._extract_profile_texture",
            return_value={"sourceSha256": "d" * 64},
        ), patch(
            "prepare_fo3_profile._race_sex_menu_tile_contract",
            return_value=race_sex_tiles,
        ):
            result = _appearance_ui_contract(
                {"opening": {"appearanceUi": definition}},
                members,
                object(),
                "e" * 64,
                Path(temporary),
                {},
            )

        self.assertEqual(result["panelName"], "NOGLOW_BRANCH")
        self.assertEqual(result["panelVisibility"], "inherited")
        self.assertEqual(result["name"]["panelName"], "TEM_MainRect")
        self.assertEqual(result["name"]["panelVisibility"], "inherited")
        tiles = result["name"]["textEditMenuTiles"]
        self.assertEqual(tiles["panel"]["rect"], [440.0, 510.0, 720.0, 180.0])
        self.assertEqual(tiles["prompt"]["text"], "Enter Name")
        self.assertEqual(tiles["prompt"]["x"]["parentFactor"], 0.5)
        self.assertEqual(tiles["input"]["y"]["selfFactor"], -0.5)
        self.assertEqual(tiles["accept"]["text"], "Ok")
        self.assertEqual(tiles["accept"]["x"]["constant"], -16.0)
        self.assertEqual(tiles["accept"]["sourceSha256"], "b" * 64)
        navigation = result["raceSexMenuTiles"]["navigation"]
        self.assertEqual(navigation["back"]["label"], "Back")
        self.assertEqual(navigation["next"]["label"], "Next")
        self.assertEqual(
            navigation["back"]["stringSourceDocuments"][0]["sha256"],
            "e" * 64,
        )


if __name__ == "__main__":
    unittest.main()
