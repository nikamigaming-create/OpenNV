using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void AuditOwnedPlayerMoverDeltaSource(FalloutPluginStack records, FalloutEarlyControllerSource controller)
    {
        var content = records.OwnedSource ?? throw new InvalidOperationException("Mover delta audit needs the actual owned record stack.");
        var source = FalloutExecutableStringTable.ReadPlayerMoverDelta(content.FalloutExecutablePath, controller);
        var values = FalloutPlayerMoverActorValueSource.Read(source, records); values.Validate();
        Console.WriteLine($"OPENNV_OWNED_PLAYER_MOVER_DELTA_SOURCE_PASS source={source.Identity} " +
            $"statsOwner={values.Player.StatsOwner} speedWord={values.Speed} bodyConditionBits={values.BodyConditionBits} " +
            "fourRealDescriptors=true actualNPCACBSCopy=true mutableGMSTFormula=true referenceDataDistinct=true " +
            "controllerNullWorldPrefix=true sourceReadOnly=true runtimeDelta=UNEXECUTED nonnullActorMovement=UNEXECUTED");
    }
}
