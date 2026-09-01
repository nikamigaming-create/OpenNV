using Godot;
using OpenNV.Runtime.Presentation.Ui;
using System.Text.Json;

var hash = new string('a', 64);
var layout = new OwnedGamebryoTileLayout(
    "menus\\dialog\\texteditmenu.xml",
    hash,
    "TEM_MainRect",
    new Rect2(440.0f, 510.0f, 720.0f, 180.0f),
    OwnedGamebryoTileVisibility.Inherited);
var text = new OwnedGamebryoTextBinding(
    "textedit_prompt",
    "-sEnterName",
    "Enter Name",
    [hash]);
OwnedGamebryoTileRuntime.Validate(layout);
OwnedGamebryoTileRuntime.Validate(text);
var placement = new OwnedGamebryoTilePlacement(
    layout.Document,
    hash,
    "textedit_button_ok",
    new OwnedGamebryoAxisExpression(1.0f, 0.0f, -16.0f),
    new OwnedGamebryoAxisExpression(1.0f, -1.0f, -16.0f),
    OwnedGamebryoHorizontalJustification.Right);
OwnedGamebryoTileRuntime.Validate(placement);
var evaluated = OwnedGamebryoTileRuntime.EvaluateTraitPosition(
    placement,
    new Vector2(720.0f, 180.0f),
    new Vector2(100.0f, 40.0f));
if (evaluated != new Vector2(604.0f, 124.0f))
    throw new InvalidOperationException(
        $"Gamebryo UI affine placement differs: {evaluated}");
var navigation = new OwnedGamebryoRaceSexNavigation(
    "RSM_next_button",
    new Vector2(300.0f, 340.0f),
    new Vector2(10.0f, 5.0f),
    64.0f,
    0.0f,
    2.0f,
    0.0f,
    OwnedGamebryoHorizontalJustification.Right,
    new OwnedGamebryoTextBinding("RSM_next_button", "-sNext", "Next", [hash]));
var navigationRect = OwnedGamebryoTileRuntime.NavigationRect(
    navigation,
    new Vector2(40.0f, 20.0f));
if (navigationRect != new Rect2(250.0f, 340.0f, 50.0f, 25.0f))
    throw new InvalidOperationException(
        $"Gamebryo UI navigation placement differs: {navigationRect}");
var options = new[]
{
    new SourceOption("000001", "Caucasian"),
    new SourceOption("000002", "Hispanic"),
};
if (OwnedGamebryoTileRuntime.RequireSourceSelection(
        options,
        option => option.FormId,
        "000002") != 1)
    throw new InvalidOperationException(
        "Gamebryo UI source selection differs.");

ExpectLayoutFailure(layout with { Rect = new Rect2(0.0f, 0.0f, 0.0f, 180.0f) });
ExpectLayoutFailure(layout with { DocumentSha256 = "unbound" });
ExpectTextFailure(text with { Text = "" });
ExpectTextFailure(text with { SourceSha256s = [] });
ExpectPlacementFailure(placement with
{
    X = new OwnedGamebryoAxisExpression(float.NaN, 0.0f, 0.0f),
});
ExpectSelectionFailure(options, "missing");
ExpectSelectionFailure(
    [new SourceOption("duplicate", "One"), new SourceOption("duplicate", "Two")],
    "duplicate");
using var dialogueDocument = JsonDocument.Parse($$"""
{
  "schema": "opennv-owned-dialogue-menu-tiles/v1",
  "document": "menus\\dialog\\dialog_menu.xml",
  "documentSha256": "{{hash}}",
  "menuName": "DialogMenu",
  "canvasSize": [1280, 720],
  "background": {
    "tile": "DM_TextBackground", "texture": "textures\\interface\\shared\\background\\solid_black.dds",
    "width": 1080, "brightness": 128, "topInset": 5, "verticalInset": 50, "heightPadding": 80
  },
  "clickTile": "DM_ClickRect",
  "speakerName": { "tile": "DM_SpeakerNameLabel", "font": 7, "rightInset": 50, "topInset": 170 },
  "speakerText": {
    "tile": "DM_SpeakerText", "font": 6, "wrapInset": 120, "leftInset": 60,
    "centerHeightFactor": 0.8, "safeBottomInset": 40
  },
  "topics": {
    "tile": "DM_TopicList", "minimumHeight": 110, "widthInset": 70, "leftInset": 20,
    "backgroundHeightPadding": 60,
    "template": { "tile": "DM_Topic", "textTile": "ListItemText", "font": 6, "textX": 25, "textY": 15, "wrapInset": 40, "verticalSpacing": 20 }
  }
}
""");
var dialogue = OwnedGamebryoTileRuntime.ParseDialogueMenu(dialogueDocument.RootElement);
if (dialogue.SpeakerTextFont != dialogue.TopicFont ||
    dialogue.BackgroundWidth != 1080.0f || dialogue.TopicMinimumHeight != 110.0f)
    throw new InvalidOperationException("Gamebryo DialogueMenu traits differ.");
var ownedFont = new OwnedBitmapFont(
    "textures\\fonts\\probe.fnt",
    32.0f,
    24.0f,
    8.0f,
    new OwnedUiTexture("probe.png", new Vector2I(1, 1)),
    [new OwnedUiGlyph(32, new Rect2(0, 0, 1, 1), Vector2.One, 0, 1, 1)]);
var dialogueFonts = OwnedGamebryoTileRuntime.RequireDialogueFonts(
    dialogue,
    new Dictionary<int, OwnedBitmapFont>
    {
        [dialogue.SpeakerNameFont] = ownedFont,
        [dialogue.SpeakerTextFont] = ownedFont,
    });
if (!ReferenceEquals(dialogueFonts.SpeakerName, ownedFont) ||
    !ReferenceEquals(dialogueFonts.Body, ownedFont))
    throw new InvalidOperationException("Gamebryo DialogueMenu font binding differs.");
ExpectDialogueFontFailure(dialogue, new Dictionary<int, OwnedBitmapFont>());
ExpectDialogueFailure(dialogueDocument.RootElement, "unsupported-dialogue-menu/v1");

Console.WriteLine(
    "OPENNV_GAMEBRYO_UI_TILE_CONTRACT_PASS layout=1 text=1 affine=1 navigation=1 selection=1 dialogue=1 fonts=1 failClosed=9");

static void ExpectDialogueFontFailure(
    OwnedGamebryoDialogueMenu menu,
    IReadOnlyDictionary<int, OwnedBitmapFont> fonts)
{
    try
    {
        OwnedGamebryoTileRuntime.RequireDialogueFonts(menu, fonts);
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException(
        "Gamebryo DialogueMenu font binding did not fail closed.");
}

static void ExpectDialogueFailure(JsonElement source, string schema)
{
    var json = source.GetRawText().Replace(
        "opennv-owned-dialogue-menu-tiles/v1",
        schema,
        StringComparison.Ordinal);
    using var document = JsonDocument.Parse(json);
    try
    {
        OwnedGamebryoTileRuntime.ParseDialogueMenu(document.RootElement);
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException("Gamebryo DialogueMenu did not fail closed.");
}

static void ExpectSelectionFailure(
    IReadOnlyList<SourceOption> options,
    string selectedFormId)
{
    try
    {
        OwnedGamebryoTileRuntime.RequireSourceSelection(
            options,
            option => option.FormId,
            selectedFormId);
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException(
        "Gamebryo UI source selection did not fail closed.");
}

static void ExpectLayoutFailure(OwnedGamebryoTileLayout source)
{
    try
    {
        OwnedGamebryoTileRuntime.Validate(source);
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException("Gamebryo UI layout did not fail closed.");
}

static void ExpectTextFailure(OwnedGamebryoTextBinding source)
{
    try
    {
        OwnedGamebryoTileRuntime.Validate(source);
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException("Gamebryo UI text did not fail closed.");
}

static void ExpectPlacementFailure(OwnedGamebryoTilePlacement source)
{
    try
    {
        OwnedGamebryoTileRuntime.Validate(source);
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException("Gamebryo UI placement did not fail closed.");
}

internal sealed record SourceOption(string FormId, string Label);
