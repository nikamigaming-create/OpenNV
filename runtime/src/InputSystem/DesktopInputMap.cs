using Godot;
using OpenNV.Runtime.Compatibility.Jam;

namespace OpenNV.Runtime.InputSystem;

internal static class DesktopInputMap
{
    internal static void Configure(DesktopInputConfiguration configuration)
    {
        foreach (var binding in configuration.KeyBindings)
        {
            using var input = CreateEvent(binding, true);
            ResetAction(binding.Action);
            InputMap.ActionAddEvent(binding.Action, input);
            var events = InputMap.ActionGetEvents(binding.Action);
            using var eventArray = (Godot.Collections.Array)events;
            if (events.Count != 1 || events[0] is not InputEventKey key ||
                key.PhysicalKeycode != ParseKey(binding.PhysicalKey))
                throw new InvalidOperationException(
                    $"Desktop key action did not retain its configured binding: {binding.Action}");
        }
        foreach (var binding in configuration.MouseBindings)
        {
            using var input = CreateEvent(binding, true);
            ResetAction(binding.Action);
            InputMap.ActionAddEvent(binding.Action, input);
            var events = InputMap.ActionGetEvents(binding.Action);
            using var eventArray = (Godot.Collections.Array)events;
            if (events.Count != 1 || events[0] is not InputEventMouseButton mouse ||
                mouse.ButtonIndex != ParseMouseButton(binding.Button))
                throw new InvalidOperationException(
                    $"Desktop mouse action did not retain its configured binding: {binding.Action}");
        }
    }

    internal static void ConfigureJamSprint(JamJvsSprintContract sprint)
    {
        ConfigureJamAction(
            JamJvsSprintContract.InputAction,
            sprint.DesktopPhysicalKey,
            "JVS sprint");
    }

    internal static void ConfigureJamBulletTime(JamJbtBulletTimeContract bulletTime)
    {
        ConfigureJamAction(
            JamJbtBulletTimeContract.InputAction,
            bulletTime.DesktopPhysicalKey,
            "JBT Bullet Time");
    }

    private static void ConfigureJamAction(
        string action,
        string physicalKey,
        string capability)
    {
        var physical = ParseKey(physicalKey);
        using var input = new InputEventKey { PhysicalKeycode = physical };
        ResetAction(action);
        InputMap.ActionAddEvent(action, input);
        var events = InputMap.ActionGetEvents(action);
        using var eventArray = (Godot.Collections.Array)events;
        if (events.Count != 1 || events[0] is not InputEventKey key ||
            key.PhysicalKeycode != physical)
            throw new InvalidOperationException(
                $"The authored {capability} key did not retain its physical binding.");
    }

    internal static InputEventKey CreateEvent(
        DesktopKeyBindingConfiguration binding,
        bool pressed)
    {
        var physical = ParseKey(binding.PhysicalKey);
        return new()
        {
            PhysicalKeycode = physical,
            Pressed = pressed,
            Echo = false,
        };
    }

    internal static InputEventMouseButton CreateEvent(
        DesktopMouseBindingConfiguration binding,
        bool pressed)
    {
        var button = ParseMouseButton(binding.Button);
        return new()
        {
            ButtonIndex = button,
            Pressed = pressed,
        };
    }

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
