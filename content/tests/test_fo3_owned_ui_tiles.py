from __future__ import annotations

import tempfile
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_fo3_profile import _appearance_ui_contract  # noqa: E402


class Fo3OwnedUiTileContractTest(unittest.TestCase):
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
            <text name="textedit_prompt"><string>&-sEnterName;</string></text>
            Interface\Shared\Background\solid_black.dds</rect></menu>
        """
        definition = {
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
        }
        with tempfile.TemporaryDirectory() as temporary, patch(
            "prepare_fo3_profile._extract_profile_texture",
            return_value={"sourceSha256": "d" * 64},
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
        self.assertEqual(
            result["name"]["prompt"],
            {
                "tile": "textedit_prompt",
                "stringEntity": "-sEnterName",
                "text": "Enter Name",
                "sourceSha256": "b" * 64,
            },
        )


if __name__ == "__main__":
    unittest.main()
