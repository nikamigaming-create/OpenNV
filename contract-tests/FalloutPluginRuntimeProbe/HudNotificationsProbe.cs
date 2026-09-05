using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class HudNotificationsProbe
{
    internal static void Run()
    {
        var queue = new FalloutHudNotifications();
        var first = new FalloutHudEvent(FalloutHudEventKind.ItemAdded, new FalloutFormKey("Synthetic.esm", 1), 3);
        var second = new FalloutHudEvent(FalloutHudEventKind.Message, new FalloutFormKey("Synthetic.esm", 2), 0);
        queue.Publish([first, second]);
        queue.Advance(0, _ => 2);
        Require(queue.Current?.Event == first, "First grant notification changed source order.");
        queue.Advance(0.75, _ => 2);
        var saved = JsonSerializer.Deserialize<FalloutHudNotificationsSnapshot>(JsonSerializer.Serialize(queue.Capture()))!;
        var restored = new FalloutHudNotifications();
        restored.Restore(saved);
        restored.Advance(0.5, _ => 2);
        Require(restored.Current?.Event == first && restored.Elapsed == 1.25, "Cold restore changed a displayed notification's clock.");
        restored.Advance(10, _ => 2);
        Require(restored.Current?.Event == second && restored.Elapsed == 0, "Delayed draw silently skipped an unseen event.");
        restored.Advance(2, _ => 2);
        Require(restored.Current is null, "Expired final notice remained visible.");
        var before = JsonSerializer.Serialize(restored.Capture());
        var failed = false;
        try { restored.Publish([first, first with { Count = 0 }]); }
        catch (InvalidDataException) { failed = true; }
        Require(failed && JsonSerializer.Serialize(restored.Capture()) == before, "Invalid event batch partially published.");
        failed = false;
        try { new FalloutHudNotifications().Restore(saved with { Pending = [saved.Current!] }); }
        catch (InvalidDataException) { failed = true; }
        Require(failed, "Duplicate saved notification order was admitted.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
