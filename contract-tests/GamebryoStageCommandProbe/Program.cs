using OpenNV.Runtime.World.Actors;

var commands = new[]
{
    Command(0, GamebryoStageCommandKind.MoveToReference, "move"),
    Command(1, GamebryoStageCommandKind.ReferenceEnabled, "enable"),
    Command(2, GamebryoStageCommandKind.AddScriptPackage, "package"),
    Command(3, GamebryoStageCommandKind.ImageSpaceModifier, "imod"),
    Command(4, GamebryoStageCommandKind.PlaySound, "sound"),
    Command(5, GamebryoStageCommandKind.PlayerControls, "controls"),
    Command(6, GamebryoStageCommandKind.SetStage, "stage"),
};
var applied = new List<string>();
GamebryoStageCommandExecutor.ExecuteAll(commands, command =>
{
    applied.Add(command.Value);
    return true;
});
if (!applied.SequenceEqual(commands.Select(command => command.Value)))
    throw new InvalidOperationException("Ordered stage execution differs.");

var selected = "";
GamebryoStageCommandExecutor.ExecuteOne(commands, 2, command =>
{
    selected = command.Value;
    return true;
});
if (selected != "package")
    throw new InvalidOperationException("Stage cursor execution differs.");

var prefix = new List<string>();
GamebryoStageCommandExecutor.ExecutePrefix(commands, commands.Length - 1, command =>
{
    prefix.Add(command.Value);
    return true;
});
if (!prefix.SequenceEqual(commands.Take(commands.Length - 1).Select(command => command.Value)))
    throw new InvalidOperationException("Stage applied-command prefix differs.");

if (!Rejects(() => GamebryoStageCommandExecutor.ExecuteAll(
        [Command(1, GamebryoStageCommandKind.SetStage, "stage")],
        _ => true)) ||
    !Rejects(() => GamebryoStageCommandExecutor.ExecuteAll(
        [Command(0, (GamebryoStageCommandKind)int.MaxValue, "unknown")],
        _ => true)) ||
    !Rejects(() => GamebryoStageCommandExecutor.ExecuteAll(
        [Command(0, GamebryoStageCommandKind.SetStage, "stage")],
        _ => false)) ||
    !Rejects(() => GamebryoStageCommandExecutor.ExecutePrefix(
        commands,
        commands.Length + 1,
        _ => true)))
    throw new InvalidOperationException("Invalid stage execution did not fail closed.");

Console.WriteLine("Gamebryo stage command probe passed.");

static SourceGamebryoStageCommand<string> Command(
    int index,
    GamebryoStageCommandKind kind,
    string value) => new(index, kind, value);

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
