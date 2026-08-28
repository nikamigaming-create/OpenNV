using Godot;

namespace OpenNV.Runtime;

internal static class DesktopInputMap
{
    internal static void Configure(DesktopInputConfiguration configuration)
    {
        foreach (var binding in configuration.KeyBindings)
        {
            ResetAction(binding.Action);
            InputMap.ActionAddEvent(binding.Action, CreateEvent(binding, true));
            var events = InputMap.ActionGetEvents(binding.Action);
            if (events.Count != 1 || events[0] is not InputEventKey key ||
                key.PhysicalKeycode != ParseKey(binding.PhysicalKey))
                throw new InvalidOperationException(
                    $"Desktop key action did not retain its configured binding: {binding.Action}");
        }
        foreach (var binding in configuration.MouseBindings)
        {
            ResetAction(binding.Action);
            InputMap.ActionAddEvent(binding.Action, CreateEvent(binding, true));
            var events = InputMap.ActionGetEvents(binding.Action);
            if (events.Count != 1 || events[0] is not InputEventMouseButton mouse ||
                mouse.ButtonIndex != ParseMouseButton(binding.Button))
                throw new InvalidOperationException(
                    $"Desktop mouse action did not retain its configured binding: {binding.Action}");
        }
    }

    internal static void ConfigureJamSprint(JamJvsSprintContract sprint)
    {
        ResetAction(JamJvsSprintContract.InputAction);
        InputMap.ActionAddEvent(
            JamJvsSprintContract.InputAction,
            new InputEventKey
            {
                PhysicalKeycode = ParseKey(sprint.DesktopPhysicalKey),
            });
        var events = InputMap.ActionGetEvents(JamJvsSprintContract.InputAction);
        if (events.Count != 1 || events[0] is not InputEventKey key ||
            key.PhysicalKeycode != ParseKey(sprint.DesktopPhysicalKey))
            throw new InvalidOperationException(
                "The authored JVS sprint key did not retain its physical binding.");
    }

    internal static InputEventKey CreateEvent(
        DesktopKeyBindingConfiguration binding,
        bool pressed) => new()
        {
            PhysicalKeycode = ParseKey(binding.PhysicalKey),
            Pressed = pressed,
            Echo = false,
        };

    internal static InputEventMouseButton CreateEvent(
        DesktopMouseBindingConfiguration binding,
        bool pressed) => new()
        {
            ButtonIndex = ParseMouseButton(binding.Button),
            Pressed = pressed,
        };

    private static void ResetAction(string action)
    {
        if (InputMap.HasAction(action))
            InputMap.EraseAction(action);
        InputMap.AddAction(action);
    }

    private static Key ParseKey(string value) =>
        Enum.Parse<Key>(value, true);

    private static MouseButton ParseMouseButton(string value) =>
        Enum.Parse<MouseButton>(value, true);
}
