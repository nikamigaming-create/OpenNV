using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutVatsValueContext(FalloutFormKey Weapon, FalloutFormKey Target,
    FalloutFormKey TargetBase, int PartActorValue, FalloutVatsAction? Action = null,
    bool? Success = null);

// Only an actual preview or queued action supplies active query values.
internal sealed class FalloutVatsConditionState(FalloutPluginStack records)
{
    private Func<bool>? _contextActive;
    private Func<FalloutVatsValueContext?>? _currentAction;
    private PreviewScope? _preview;
    internal FalloutVatsValueContext? CurrentContext => _preview?.Context ?? _currentAction?.Invoke();

    internal void BindContext(Func<bool> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        if (_contextActive is not null) throw new InvalidOperationException("VATS context already has a live owner.");
        _contextActive = active;
    }

    internal void BindCurrentAction(Func<FalloutVatsValueContext?> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_currentAction is not null) throw new InvalidOperationException("VATS action already has a live owner.");
        _currentAction = current;
    }

    internal IDisposable BeginPreview(FalloutVatsValueContext context)
    {
        if (context.Action is not null || context.Success is not null)
            throw new InvalidDataException("VATS preview cannot fabricate a queued action result.");
        RequireSources(context);
        return _preview = new(this, context, _preview);
    }

    private void RequireSources(FalloutVatsValueContext context)
    {
        if (context.Target.ObjectId == 0 || records.GetEffective(context.Weapon).Signature != "WEAP" ||
            records.GetEffective(context.TargetBase).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("VATS context has no actual weapon/target source.");
        if (context.Action is { } action && !Enum.IsDefined(action))
            throw new NotSupportedException("VATS context has no qualified special-action producer.");
    }

    internal float Query(FalloutConditionQuery condition, IFalloutFormLists? lists = null)
    {
        var context = CurrentContext;
        if (context is null)
        {
            // A bound action owner can authoritatively report no current action;
            // an activity boolean alone cannot supply that negative knowledge.
            if (_currentAction is not null || _contextActive?.Invoke() != true) return 0;
            throw Missing(condition);
        }
        RequireSources(context);
        return condition.Argument1 switch
        {
            0 => FormOperand(condition) == context.Weapon ? 1 : 0,
            1 => InList(condition, context.Weapon, lists) ? 1 : 0,
            2 => FormOperand(condition) == context.TargetBase ? 1 : 0,
            3 => InList(condition, context.TargetBase, lists) ? 1 : 0,
            5 => unchecked((uint)context.PartActorValue) == condition.Argument2 ? 1 : 0,
            6 when context.Action is { } action => (uint)action == condition.Argument2 ? 1 : 0,
            7 when context.Success is { } success => success ? 1 : 0,
            _ => throw Missing(condition)
        };
    }

    private FalloutFormKey? FormOperand(FalloutConditionQuery condition)
    {
        if (condition.BoundSecondForm is { } bound ? bound.Number == 0 : condition.Argument2 == 0) return null;
        var form = condition.FormArgument2(records);
        FalloutFormList.RequireMember(records, form);
        return form;
    }

    private bool InList(FalloutConditionQuery condition, FalloutFormKey form, IFalloutFormLists? lists)
    {
        if (FormOperand(condition) is not { } list) return false;
        var result = false;
        foreach (var member in FalloutFormList.Entries(records, list, lists)) result |= member == form;
        return result;
    }

    private static NotSupportedException Missing(FalloutConditionQuery condition) =>
        new($"GetVATSValue selector {condition.Argument1}/{condition.Argument2} requires its current VATS action-context value owner.");

    internal float Mode() => _contextActive?.Invoke() != true ? 0 :
        throw new NotSupportedException("Active VATS camera mode requires its native phase owner.");

    internal float TargetHeight()
    {
        // Calculation values do not own the actual camera/session phase.
        if (_contextActive?.Invoke() != true) return 0;
        if (_currentAction is { } current)
        {
            if (current() is null) return 0;
            throw new NotSupportedException("VATS playback requires its source node/root-bound target-height owner.");
        }
        throw new NotSupportedException("Active VATS target height requires its attack-camera target owner.");
    }

    private sealed class PreviewScope(FalloutVatsConditionState owner, FalloutVatsValueContext context,
        PreviewScope? previous) : IDisposable
    {
        private bool _disposed;
        internal FalloutVatsValueContext Context => context;
        public void Dispose()
        {
            if (_disposed) return;
            if (!ReferenceEquals(owner._preview, this)) throw new InvalidOperationException("VATS preview scopes retired out of order.");
            owner._preview = previous;
            _disposed = true;
        }
    }
}
