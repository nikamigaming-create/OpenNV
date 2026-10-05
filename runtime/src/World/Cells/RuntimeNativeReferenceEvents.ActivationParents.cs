using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferenceEvents
{
    private readonly Dictionary<FalloutFormKey, string> _activationRelayErrors = [];
    private readonly HashSet<FalloutFormKey> _dispatchingActivationRelays = [];
    private void QueueActivationRelays(float seconds)
    {
        foreach (var delivery in _world.AdvanceActivationRelays(seconds))
        {
            var reference = delivery.Child.Reference;
            if (!_bindings.TryGetValue(reference, out var binding) || !_world.IsResident(reference))
            {
                var error = "Activation-parent child has no resident event owner.";
                if (_activationRelayErrors.GetValueOrDefault(reference) != error)
                    ReportDivergence($"OPENNV_NATIVE_ACTIVATION_RELAY_UNBOUND parent={delivery.Parent} child={reference}: {error}");
                _activationRelayErrors[reference] = error;
                continue; // Keep the due receipt; unloading never fabricates delivery.
            }
            if (binding.PendingActivation is not null || _dispatchingActivationRelays.Contains(reference)) continue;
            if (!_world.CanActivate(reference)) { _world.ConsumeActivationRelay(delivery); continue; }
            binding.PendingActivation = delivery.Child.ActionReference;
            binding.PendingRelay = delivery;
            binding.PendingPlayerInput = false;
            _activationRelayErrors.Remove(reference);
        }
    }
}
