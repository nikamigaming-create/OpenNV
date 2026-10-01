using System.Buffers.Binary;
using System.Globalization;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// The selected INI chooses a quest, not a CELL or marker. Its authored stage
// results and menu scripts publish the first player movement through the same
// world owner used after startup. The native host supplies blocking presentation.
internal sealed class FalloutNewGameBootstrap
{
    private readonly FalloutQuestState _quests;
    private readonly FalloutQuestScripts _scripts;
    private readonly FalloutReferenceWorld _world;
    private readonly FalloutQuestStages _stages;
    private readonly FalloutPluginStack _records;
    private bool _started;
    internal FalloutPluginRecord Quest { get; }
    internal FalloutPlayerControlState Controls { get; private set; } = FalloutPlayerControlState.AllEnabled;
    internal FalloutQuestScriptHost Host { get; }
    internal object State => new { quest = Quest.FormKey, started = _started, controls = Controls, stages = _stages.Errors };

    internal static FalloutPluginRecord StartingQuest(FalloutPluginStack records, FalloutInstallationSettings settings)
    {
        var value = settings.Require("General", "SCharGenQuest");
        if (!uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var id) || id == 0)
            throw new InvalidDataException("[General] SCharGenQuest is not a hexadecimal FormID.");
        var quest = records.GetEffective(records.RuntimeFormKey(id));
        if (quest.Signature != "QUST") throw new InvalidDataException("Configured character-generation form is not QUST.");
        return quest;
    }

    internal FalloutNewGameBootstrap(FalloutPluginStack records, FalloutInstallationSettings settings,
        FalloutQuestState quests, FalloutQuestScripts scripts, FalloutReferenceWorld world,
        Action<FalloutFormKey, FalloutScriptBindings, string, IReadOnlyList<string>> command,
        Action<FalloutReferenceScriptEffect> effect, Func<bool> canContinue, FalloutGlobalState? globals = null)
    {
        _records = records; _quests = quests; _scripts = scripts; _world = world;
        Quest = StartingQuest(records, settings);
        var executor = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => throw new NotSupportedException("Startup furniture query has no resident actor."), Apply,
                scripts.MessageResults.Take, Globals: globals, Command: command, Events: scripts.Events,
                LocationSpecificLoadScreensOnly: () => scripts.Session.LocationSpecificLoadScreensOnly,
                InCharGen: () => scripts.Session.InCharGen));
        _stages = new(records, quests, executor.StageSteps,
            condition => FalloutPlatformConditions.Evaluate(condition) ?? quests.Evaluate(condition), canContinue);
        Host = new((quest, stage) => () => _stages.Enter(quest, stage),
            _ => throw new NotSupportedException("Startup player actor-value query has no player state owner."),
            executor.ExecuteProgram, executor.InvokeFunction);

        void Apply(FalloutReferenceScriptEffect change)
        {
            switch (change.Kind)
            {
                case FalloutReferenceEffectKind.ReferenceEnable when !world.IsResident(change.Target!.Value):
                    // The reference executor already changed authoritative state.
                    break;
                case FalloutReferenceEffectKind.SetStage:
                    _stages!.Enter(change.Target!.Value, change.Stage);
                    break;
                case FalloutReferenceEffectKind.PlayerControls:
                    Controls = new FalloutPlayerControlCommand(change.Enable, change.Controls ??
                        throw new InvalidDataException("Startup controls have no mask.")).Apply(Controls);
                    break;
                case FalloutReferenceEffectKind.LoadingScreenPolicy:
                    scripts.Session.LocationSpecificLoadScreensOnly = change.Enable;
                    break;
                case FalloutReferenceEffectKind.CharacterGeneration:
                    scripts.Session.SetInCharGen(change.Enable, null);
                    break;
                case FalloutReferenceEffectKind.Message:
                    var owner = records.GetEffective(change.Source);
                    var caller = owner.Signature == "QUST" ? FalloutScriptLocals.AttachedScript(records, owner)?.FormKey :
                        world.Get(change.Source).Script?.Record.FormKey;
                    scripts.ShowMessage(change.Target!.Value, caller, owner.Signature == "QUST" ? null : change.Source);
                    break;
                default:
                    effect(change);
                    break;
            }
        }
    }

    internal void Start()
    {
        if (_started) throw new InvalidOperationException("New-game bootstrap was already started.");
        _started = true;
        _scripts.Host = Host;
        _quests.SetRunning(Quest.FormKey, true);
        var zero = Quest.ReadSubrecords().Any(field => field.Signature == "INDX" && field.Data.Length == 2 &&
            BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span) == 0);
        if (zero) _stages.Enter(Quest.FormKey, 0);
        else if (FalloutScriptLocals.AttachedScript(_records, Quest) is null)
            throw new NotSupportedException("Configured startup quest has neither stage zero nor an attached script.");
        _scripts.RequireQuestExecution(Quest.FormKey);
    }

    internal void Advance(double seconds, IEnumerable<uint> menus)
    {
        if (!_started) throw new InvalidOperationException("New-game bootstrap has not started.");
        _stages.Continue();
        _scripts.Advance(seconds, gameMode: false, menus: menus);
        _scripts.RequireQuestExecution(Quest.FormKey);
    }

    internal FalloutReferencePlacement? Placement()
    {
        if (_world.PlayerMoves.Error is { } error) throw new NotSupportedException(error);
        if (_world.PlayerMoves.Next is not { } move) return null;
        if (move.Destination == _records.RuntimeFormKey(0x14))
            throw new NotSupportedException("Startup self movement has no prior player placement.");
        return _world.ResolvePlayerMove(move, _world.Placement(move.Destination), 1);
    }
}
