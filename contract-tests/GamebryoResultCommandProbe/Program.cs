using OpenNV.Runtime.World.Actors;

var applied = new List<string>();
var commands = new[]
{
    Command(0, GamebryoResultCommandKind.SetQuestVariable, false, "reaction"),
    Command(1, GamebryoResultCommandKind.ReferenceEnabled, false, "doctor"),
    Command(2, GamebryoResultCommandKind.PlayerControls, false, "controls"),
    Command(3, GamebryoResultCommandKind.SetStage, true, "stage"),
};
var execution = GamebryoResultCommandExecutor.Execute(
    commands,
    0,
    command =>
    {
        applied.Add(command.Value);
        return true;
    });
if (!execution.Terminal || execution.AppliedCount != commands.Length ||
    !applied.SequenceEqual(["reaction", "doctor", "controls", "stage"]))
    throw new InvalidOperationException("Ordered result execution differs.");

if (!Rejects(() => GamebryoResultCommandExecutor.Execute(
        [Command(1, GamebryoResultCommandKind.SetStage, true, "stage")],
        0,
        _ => true)) ||
    !Rejects(() => GamebryoResultCommandExecutor.Execute(
        [
            Command(0, GamebryoResultCommandKind.SetStage, true, "stage"),
            Command(1, GamebryoResultCommandKind.ActorIntent, false, "evp"),
        ],
        0,
        _ => true)) ||
    !Rejects(() => GamebryoResultCommandExecutor.Execute(
        [Command(0, GamebryoResultCommandKind.SetStage, true, "stage")],
        0,
        _ => false)) ||
    !Rejects(() => GamebryoResultCommandExecutor.Execute(
        [Command(0, (GamebryoResultCommandKind)int.MaxValue, false, "unknown")],
        0,
        _ => true)))
    throw new InvalidOperationException("Invalid result execution did not fail closed.");

Console.WriteLine("Gamebryo result command probe passed.");

static SourceGamebryoResultCommand<string> Command(
    int index,
    GamebryoResultCommandKind kind,
    bool terminal,
    string value) => new(index, kind, terminal, value);

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
