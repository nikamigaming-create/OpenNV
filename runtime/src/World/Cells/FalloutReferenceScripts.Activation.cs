using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    internal bool CanAdmitActivation(FalloutFormKey reference)
    {
        var instance = world.Get(reference);
        return instance.ScriptError is not { } error ||
            error.StartsWith("OnActivate:", StringComparison.OrdinalIgnoreCase) || HasIndependentDefaultActivation(instance);
    }

    private bool HasIndependentDefaultActivation(FalloutReferenceInstance instance)
    {
        if (instance.Script is null) return true;
        try
        {
            return !Program(instance).Events.Any(block => block.Event.Equals("OnActivate", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException)
        {
            // An unreadable source program cannot establish the absence of an
            // activation block. Its ordinary script admission remains unchanged.
            return false;
        }
    }

    private IReadOnlyList<FalloutReferenceScriptEventResult> DispatchIndependentDefaultActivation(
        FalloutReferenceInstance instance, IReadOnlyList<FalloutReferenceScriptEvent> events,
        FalloutReferenceScriptEvent activation, double elapsedSeconds,
        Action? observeActivationBegin, Action? observeActivationEnd)
    {
        string? activationError = null;
        var stoppedError = instance.ScriptError;
        try
        {
            if (!world.CanActivate(instance.Reference))
                throw new InvalidOperationException("Reference is no longer available for default activation.");
            observeActivationBegin?.Invoke();
            host.Apply(new(FalloutReferenceEffectKind.DefaultActivate, instance.Reference, instance.Reference, activation.ActionReference));
            observeActivationEnd?.Invoke();
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException)
        {
            // Default interaction is independent of the attached source VM.
            // Its failure neither replaces nor creates a stopped script error.
            activationError = "OnActivate: " + error.Message;
        }
        var remaining = events.Where(item => !item.Name.Equals("OnActivate", StringComparison.OrdinalIgnoreCase)).ToArray();
        var results = (stoppedError is null ? DispatchFrameCore(instance, remaining, elapsedSeconds) :
            remaining.Select(item => new FalloutReferenceScriptEventResult(instance.Reference, item.Name, 0, stoppedError)).ToArray())
            .ToDictionary(result => FalloutReferencePackageEvents.CanonicalName(result.Event), StringComparer.OrdinalIgnoreCase);
        return events.Select(item => item.Name.Equals("OnActivate", StringComparison.OrdinalIgnoreCase)
            ? new FalloutReferenceScriptEventResult(instance.Reference, item.Name, 0, activationError)
            : results[FalloutReferencePackageEvents.CanonicalName(item.Name)]).ToArray();
    }
}
