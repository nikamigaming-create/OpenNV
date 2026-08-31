using OpenNV.Runtime.World.Actors;

const string quest = "00000001";
var state = new GamebryoPackageState(
    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [quest] = 20 },
    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
var rows = new[]
{
    Candidate("00000010", 10, "first"),
    Candidate("00000020", 20, "second"),
};
var selected = GamebryoPackageSelector.SelectFirst(rows, state, requireMatch: true);
if (selected?.Value != "second")
    throw new InvalidOperationException("Ordered source package selection differs.");

var noMatch = GamebryoPackageSelector.SelectFirst(
    [Candidate("00000010", 10, "first")],
    state,
    requireMatch: false);
if (noMatch is not null)
    throw new InvalidOperationException("Optional source package selection invented a match.");

var unsupportedRejected = false;
try
{
    GamebryoPackageSelector.SelectFirst(
        [
            new GamebryoPackageCandidate<string>(
                "00000030",
                [],
                GamebryoPackageTarget.None,
                null,
                "first"),
            new GamebryoPackageCandidate<string>(
                "00000040",
                [new GamebryoPackageCondition(
                    "unsupported",
                    GamebryoPackageComparison.Equal,
                    0.0,
                    quest,
                    0,
                    0,
                    "")],
                GamebryoPackageTarget.None,
                null,
                "second"),
        ],
        state,
        requireMatch: true);
}
catch (InvalidOperationException)
{
    unsupportedRejected = true;
}
if (!unsupportedRejected)
    throw new InvalidOperationException("Unsupported lower-priority package was admitted.");

if (!Rejects(() => GamebryoPackageSelector.SelectFirst(
        [new GamebryoPackageCandidate<string>(
            "00000050",
            [],
            new GamebryoPackageTarget("packageTarget:reference", "00000060"),
            null,
            "target")],
        state,
        requireMatch: true)))
    throw new InvalidOperationException("Unsupported source package target was admitted.");

if (!Rejects(() => GamebryoPackageSelector.SelectFirst(
        [new GamebryoPackageCandidate<string>(
            "00000070",
            [],
            GamebryoPackageTarget.None,
            new SourceActorAnimation(
                "animation.kf",
                "hash",
                "animation",
                0.0f,
                1.0f,
                1,
                "owned-world-root-authoritative-zero-local-translation"),
            "animation")],
        state,
        requireMatch: true)))
    throw new InvalidOperationException("Unsupported source package timing was admitted.");

Console.WriteLine("GAMEBRYO_PACKAGE_SELECTION_PROBE_PASS selected=00000020");
return;

static GamebryoPackageCandidate<string> Candidate(
    string formId,
    int stage,
    string value) => new(
    formId,
    [new GamebryoPackageCondition(
        "getStage",
        GamebryoPackageComparison.Equal,
        stage,
        "00000001",
        0,
        0,
        "")],
    GamebryoPackageTarget.None,
    null,
    value);

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
