using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private bool _sourceLinkedPlayerParentConstructed;
    private FalloutFormKey? _sourceLinkedPlayerParent;
    private void ConstructSourcePlayerLinkedParent(bool cold)
    {
        var actor = ReadCombatActorIdentity(_enginePlayer);
        if (_sourceLinkedPlayerParentConstructed || !actor.EnginePlayer || !ActorProcesses.HasActor(_enginePlayer))
            throw new InvalidOperationException("Canonical Player ParentCELL has no actual once-only source actor factory.");
        // Source reference construction initializes ParentCELL to null. Cold
        // resumes the returned source setter; native body publication is later.
        _sourceLinkedPlayerParent = cold ? SourceCellLinks.RuntimeActorParent(actor) : null;
        _sourceLinkedPlayerParentConstructed = true;
    }
    internal FalloutFormKey? CurrentSourcePlayerLinkedCell
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sourceLinkedPlayerParentConstructed)
                throw new NotSupportedException("Canonical Player source ParentCELL constructor is absent.");
            return _sourceLinkedPlayerParent;
        }
    }
    private FalloutCombatActorIdentity RequirePublishedLinkedPlayer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var actor = ReadCombatActorIdentity(_enginePlayer);
        if (!actor.EnginePlayer || ReadActualProcessBody(actor.Reference) is not { } body || body.Actor != actor.Reference ||
            _currentPlayerProcessCellLease is null || _currentPlayerProcessCell is null)
            throw new NotSupportedException("Canonical linked Player has no actual source/native-body and ParentCELL lifetime.");
        return actor;
    }

    internal void CommitSourcePlayerLinkedCell(FalloutMainPlayerCellInvocation invocation,
        FalloutFormKey destination, Action publishActualParentCell)
    {
        var step = invocation.Step;
        if (step is not (FalloutMainPlayerCellStep.PendingDestination or FalloutMainPlayerCellStep.CellAttach))
            throw new InvalidOperationException("Canonical CELL insertion has no actual Player destination/attachment child.");
        RequireCampaignMainPlayerInvocation(invocation, step);
        var actor = RequirePublishedLinkedPlayer();
        var before = CurrentSourcePlayerLinkedCell;
        if (_currentPlayerProcessCell!() != before)
            throw new InvalidDataException("Canonical Player native ParentCELL lease substituted the active presentation CELL for source membership.");
        SourceCellLinks.RequireRuntimeActorParent(actor, before);
        SourceCellLinks.InsertRuntimeActor(actor, before, destination, () =>
        {
            RequireCampaignMainPlayerInvocation(invocation, step);
            _ = RequirePublishedLinkedPlayer(); publishActualParentCell();
            _sourceLinkedPlayerParent = destination;
            if (_currentPlayerProcessCell!() != destination)
                throw new InvalidDataException("Canonical Player ParentCELL callback did not publish the actual destination.");
        }, "actual-source-Player-" + step + "/" + invocation.Main.Identity);
    }

    // Cold native construction rebinds a retained source membership. It never
    // inserts the Player again or changes the source list's membership order.
    internal void RebindSourcePlayerLinkedCell(FalloutFormKey cell)
    {
        var actor = RequirePublishedLinkedPlayer();
        if (_currentPlayerProcessCell!() != cell)
            throw new InvalidDataException("Cold linked Player publication differs from its actual ParentCELL.");
        SourceCellLinks.RequireRuntimeActorParent(actor, cell);
    }

    private uint ReadSourceCanonicalPlayerReferenceFlags()
    {
        _ = RequirePublishedLinkedPlayer();
        var declaration = FalloutSourceCellLoaderDeclaration.Read(_processRuntimeDeclaration!.ExecutableSha256);
        var flags = declaration.CanonicalPlayerInitialReferenceFlags;
        // This writer is the real source manager pending-request owner. The
        // immutable ENGINE_PLAYER identity is not a current-reference flag.
        var pending = ProcessReevaluation.ReadPendingReferenceFlag(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch);
        if (pending.Require()) flags |= FalloutActorProcessQueueDeclaration.PendingReferenceFlag;
        return flags;
    }
}
