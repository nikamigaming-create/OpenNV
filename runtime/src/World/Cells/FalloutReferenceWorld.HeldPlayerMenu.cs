using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutSourceHeldPlayerMenu? _standaloneHeldPlayerMenu;
    internal FalloutHeldPlayerMenuCall? StandaloneHeldPlayerMenuState => _standaloneHeldPlayerMenu?.LastCall;

    internal FalloutPlayerControlHudFrame ReadSourceInterfaceMenuFrame()
    {
        RequireStandaloneControlPublication();
        return (_standaloneControlHudContext ?? throw new InvalidOperationException("Source menu has no actual HUD/timer context."))
            .ReadTransitionFrame();
    }

    internal void ExecuteStandaloneHeldPlayerMenu(FalloutMainPlayerCellInvocation invocation)
    {
        RequireHeldChild(invocation);
        _standaloneHeldPlayerMenu ??= new(
            StandaloneInterface.ReadBoundMenuLifecycleSource(),
            RequireHeldChild, ReadStandaloneSceneProcessQueries,
            call => _sourcePackageFields[_enginePlayer].ReadHeldPlayerMenuFirstContext(call, RequireHeldChild));
        _standaloneHeldPlayerMenu.Execute(invocation);
    }

    private void RequireHeldChild(FalloutMainPlayerCellInvocation invocation)
    {
        RequireStandaloneControlPublication();
        RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.HeldChild);
        var actor = ActorProcesses.Read(_enginePlayer);
        if (!_sourcePackageFields.TryGetValue(_enginePlayer, out var fields) || fields.Actor != actor.Source ||
            fields.Epoch != actor.Epoch || fields.Stack != _processRuntimeStack ||
            fields.Source.Controls.Main != CampaignStandalonePlayerScene.Source.Main)
            throw new InvalidOperationException("Held Player menu lost its actual Actor/field/epoch/source ownership.");
        var current = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        if (fields.Process != current.Process)
            throw new InvalidOperationException("Held menu fields no longer belong to the actual Common process.");
    }
}
