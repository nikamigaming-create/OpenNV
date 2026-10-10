using System.Text.Json;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerFurnitureAudit
{
    private static void CheckActualFurnitureActivation(RuntimeNativePlayer player)
    {
        string Phase() => JsonSerializer.SerializeToElement(player.FurnitureState).GetProperty("phase").GetString()!;
        if (Phase() != "occupied") throw new InvalidOperationException("Native activation check requires the genuine occupied source seat.");
        var control = player.SourceControls;
        var first = player.AcquireModalInput(); var second = player.AcquireModalInput();
        try
        {
            first(); player.XrActivate();
            if (!player.ModalInput || Phase() != "occupied")
                throw new InvalidOperationException("One dialogue release erased a different live modal owner or entered the source exit.");
        }
        finally { first(); second(); }
        if (player.ModalInput) throw new InvalidOperationException("Completed native modal leases retained an anonymous input blocker.");
        try
        {
            player.ApplySourceControls(control with { Movement = false }); player.XrActivate();
            if (Phase() != "occupied") throw new InvalidOperationException("XR activation bypassed source-disabled controls.");
        }
        finally { player.ApplySourceControls(control); }
        player.XrActivate();
        if (Phase() != "exiting" || !player.FurnitureActive)
            throw new InvalidOperationException("Actual XR activation did not enter the same genuine source exit KF as flat input.");
        // The existing fixture must still observe the real KF clock, source
        // text keys, final transform and released reservation before its pass.
    }
}
