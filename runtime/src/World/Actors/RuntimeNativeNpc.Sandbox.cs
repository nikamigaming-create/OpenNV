using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutSandboxPackage? _sandboxPackage;
    private FalloutWanderProgress? _sandboxWander;

    private void BeginSandbox(FalloutPluginRecord package, bool initializing)
    {
        var source = FalloutSandboxPackage.Read(package);
        var retained = initializing && Combat!.PackageMotion?.Package == package.FormKey
            ? Combat.PackageMotion.Wander : null;
        var restoring = retained is not null || initializing && Combat!.PackageMotion?.Package == package.FormKey &&
            Combat.PackageMotion.Sandbox is not null;
        if (retained is not null) _aiWorld!.ValidatePackageWander(Appearance.Reference!.Value, retained);
        _sandboxPackage = source; _sandboxWander = retained;
        _aiPackage = package; _packageIdles = null;
        _aiReferenceState!.ProcedureCaptureBlocker = !restoring
            ? FalloutSandboxPackage.CaptureBlockerPrefix + "awaiting first native movement observation." : null;
        if (!restoring) _packageEvents!.Change(_packageIdleSource);
        else _packageEvents!.Restore(_packageIdleSource!, false);
    }

    private void AdvanceSandbox(double delta)
    {
        if (_sandboxPackage is not { } source || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try { _sandboxWander = Combat.AdvanceSandboxWander(_aiPackage!, source, _sandboxWander, delta); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            _aiReferenceState!.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + error.Message;
            Godot.GD.PushError($"OPENNV_NATIVE_SANDBOX_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }
}
