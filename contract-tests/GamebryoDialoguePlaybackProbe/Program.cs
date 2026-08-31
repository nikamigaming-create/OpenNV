using OpenNV.Runtime.World.Actors;

var temporary = Path.GetTempFileName();
try
{
    var asset = new SourceDialogueAsset("sound\\voice\\line.ogg", temporary, new string('a', 64));
    GamebryoDialoguePlayback.ValidateOrderedLines(
    [
        new SourceDialogueLine("00000001", 1, "00000010", "First", asset, asset),
        new SourceDialogueLine("00000001", 2, "00000010", "Second", asset, asset),
        new SourceDialogueLine("00000002", 1, "00000020", "Third", asset, asset),
    ]);
    var result = GamebryoDialoguePlayback.RequireStageResult(["setstage CG00 22"]);
    if (result.QuestEditorId != "CG00" || result.Stage != 22)
        throw new InvalidOperationException("Source dialogue stage handoff differs.");
    var typedResult = GamebryoDialoguePlayback.RequireStageResult(
        "setStage", "00104c1c", 2);
    if (typedResult.QuestFormId != "00104c1c" || typedResult.Stage != 2)
        throw new InvalidOperationException("Typed source stage handoff differs.");
    var infos = new[]
    {
        new SourceDialogueInfoCandidate<string, bool>("00000001", 0, true, [true], "said"),
        new SourceDialogueInfoCandidate<string, bool>("00000002", 1, false, [false], "blocked"),
        new SourceDialogueInfoCandidate<string, bool>("00000003", 2, false, [true], "selected"),
    };
    var selection = GamebryoDialoguePlayback.SelectFirstInfo(
        infos,
        0,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "00000001" },
        value => value);
    if (selection?.Value != "selected" || selection.NextCursor != 3)
        throw new InvalidOperationException("Ordered source INFO selection differs.");
    if (!Rejects(() => GamebryoDialoguePlayback.RequireStageResult(
            ["setstage CG00 22", "set foo to 1"])) ||
        !Rejects(() => GamebryoDialoguePlayback.RequireStageResult(["startquest CG00"])) ||
        !Rejects(() => GamebryoDialoguePlayback.ValidateOrderedLines(
        [
            new SourceDialogueLine("00000001", 2, "00000010", "Second", asset, asset),
            new SourceDialogueLine("00000001", 1, "00000010", "First", asset, asset),
        ])))
        throw new InvalidOperationException("Unsupported dialogue semantics did not fail closed.");
}
finally
{
    File.Delete(temporary);
}

Console.WriteLine("Gamebryo dialogue playback probe passed.");

static bool Rejects(Action action)
{
    try
    {
        action();
        return false;
    }
    catch (InvalidOperationException)
    {
        return true;
    }
}
