using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private RuntimeNativeSourceMainScriptCaller? _sourceMainScriptCaller;
    private bool _sourceMainScriptCallerRetired;
    internal object? SourceMainScriptCallerState => _sourceMainScriptCaller?.State;
    internal void AttachSourceMainScriptCaller()
    {
        var world = _scripts.References ?? throw new InvalidOperationException("Main caller has no actual campaign world.");
        if (world.Challenges.Source is null) return;
        if (_sourceMainScriptCaller is not null || _sourceMainScriptCallerRetired || !IsInsideTree())
            throw new InvalidOperationException("Source Main native delivery cannot attach twice or before the living driver.");
        _sourceMainScriptCaller = RuntimeNativeSourceMainScriptCaller.Attach(this, world,
            _sourceInterfaceFade ?? throw new NotSupportedException("Actual source Main needs the same constructed rest/interface fade owner."),
            RetainDriverFailure);
    }
    internal void StartSourceMainScriptCaller()
    {
        if (_scripts.References!.Challenges.Source is null) return;
        (_sourceMainScriptCaller ?? throw new NotSupportedException("Actual native Main source caller is absent.")).Start();
    }
    internal void RetireSourceMainScriptCaller()
    {
        if (_sourceMainScriptCallerRetired) return;
        var native = _sourceMainScriptCaller;
        if (native is null) { _sourceMainScriptCallerRetired = true; return; }
        var failures = new List<Exception>();
        try { native.Retire(); } catch (Exception failure) { failures.Add(failure); }
        try
        {
            if (native.CanDestroyAfterRetirement && GodotObject.IsInstanceValid(native)) native.Free();
            if (!GodotObject.IsInstanceValid(native)) { _sourceMainScriptCaller = null; _sourceMainScriptCallerRetired = true; }
        }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Source Main native retirement retained its actual failure.", failures);
    }
}
