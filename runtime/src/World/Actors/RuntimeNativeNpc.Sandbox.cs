using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutSandboxPackage? _sandboxPackage;
    private FalloutWanderProgress? _sandboxWander;

    private void BeginSandbox(FalloutPluginRecord package, bool initializing)
    {
        ClearSandboxAdmissionFailure();
        ClearPendingSandboxStart(clearBlocker: false);
        var source = FalloutSandboxPackage.Read(package);
        var retained = initializing && Combat!.PackageMotion?.Package == package.FormKey
            ? Combat.PackageMotion.Wander : null;
        var restoring = retained is not null || initializing && Combat!.PackageMotion?.Package == package.FormKey &&
            Combat.PackageMotion.Sandbox is not null;
        if (retained is not null) _aiWorld!.ValidatePackageWander(Appearance.Reference!.Value, retained);
        _sandboxPackage = source; _sandboxWander = retained;
        _aiPackage = package; _packageIdles = null;
        _aiReferenceState!.ProcedureCaptureBlocker = !restoring
            ? FalloutActorPendingSandboxStart.CaptureBlocker : null;
        if (!restoring)
        {
            _packageEvents!.Change(_packageIdleSource);
            _pendingSandboxAwaitingFirstMotion = true;
        }
        else _packageEvents!.Restore(_packageIdleSource!, false);
    }

    private void AdvanceSandbox(double delta)
    {
        if (_sandboxPackage is not { } source || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null ||
            _pendingSandboxStagedReceipt is not null && !_pendingSandboxPublicationComplete) return;
        var previousMotion = Combat.PackageMotion;
        var completed = false;
        try
        {
            _sandboxWander = Combat.AdvanceSandboxWander(_aiPackage!, source, _sandboxWander, delta);
            completed = true;
            // Dialogue-busy admission returns before selecting an activity.
            // Keep that genuine untouched Start; only a new observation retires it.
            if (Combat.PackageMotion is { } motion && motion.Package == _aiPackage!.FormKey && !ReferenceEquals(motion, previousMotion))
                ClearPendingSandboxStart(clearBlocker: true);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            ClearPendingSandboxStart(clearBlocker: false);
            _sandboxAdmissionFailure = error as FalloutSandboxAdmissionException;
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            _aiReferenceState!.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + error.Message;
            Godot.GD.PushError($"OPENNV_NATIVE_SANDBOX_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
        finally
        {
            // Any failed attempt can already have consumed activity/RNG or
            // applied part of a pose. Never fall back to an untouched Start.
            if (!completed)
            {
                ClearPendingSandboxStart(clearBlocker: false);
                if (_aiError is null)
                    _aiReferenceState!.ProcedureCaptureBlocker = FalloutSandboxPackage.CaptureBlockerPrefix + "first native observation did not complete.";
            }
        }
    }
}
