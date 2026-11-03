using System.Globalization;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private void BindRadiationPanel(GameplayVitals vitals)
    {
        Bind("StatsMenu", "user1", FalloutRadiationStage.DisplayRads(vitals.RadiationRads));
        Bind("StatsMenu", "user2", FalloutRadiationStage.MenuMaximum(_records));
        _tiles.BindText(_tiles.Root, "_rads", Setting("sHUDRads").ToUpperInvariant());
        var resistance = (_actorValue ?? throw new NotSupportedException("RAD panel has no actual RadResist owner."))("RadResist");
        var maximum = _records.NumericSettings.Float("fPlayerMaxResistance");
        if (!float.IsFinite(resistance) || !float.IsFinite(maximum) || maximum < int.MinValue || (double)maximum > int.MaxValue ||
            (double)MathF.Floor(resistance) < int.MinValue || (double)MathF.Floor(resistance) > int.MaxValue)
            throw new InvalidDataException("RAD panel resistance exceeds source integer storage.");
        var shown = Math.Min((int)MathF.Floor(resistance), (int)maximum);
        Text("stats_rad_resist_level", shown.ToString(CultureInfo.InvariantCulture) + "%");
    }

    private void BindEffectsPanel()
    {
        throw new NotSupportedException("EFF source list completeness requires its ability/equipment/ingestible/poison visibility owner.");
    }
}
