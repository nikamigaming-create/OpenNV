using Godot;
using OpenNV.Runtime.Presentation.Ui;

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

Console.WriteLine(
    "OPENNV_GAMEBRYO_UI_TILE_CONTRACT_PASS layout=1 text=1 affine=1 navigation=1 selection=1 failClosed=7");

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
