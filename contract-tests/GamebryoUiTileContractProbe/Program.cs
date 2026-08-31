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

ExpectLayoutFailure(layout with { Rect = new Rect2(0.0f, 0.0f, 0.0f, 180.0f) });
ExpectLayoutFailure(layout with { DocumentSha256 = "unbound" });
ExpectTextFailure(text with { Text = "" });
ExpectTextFailure(text with { SourceSha256s = [] });
ExpectPlacementFailure(placement with
{
    X = new OwnedGamebryoAxisExpression(float.NaN, 0.0f, 0.0f),
});

Console.WriteLine(
    "OPENNV_GAMEBRYO_UI_TILE_CONTRACT_PASS layout=1 text=1 affine=1 failClosed=5");

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
