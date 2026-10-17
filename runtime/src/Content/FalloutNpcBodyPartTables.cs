namespace OpenNV.Runtime.Content;

// The NPC virtual uses the loaded default table, except that the Player's
// current Base may select the independently loaded Player table. An absent or
// failed Player-table cast leaves that override null; it does not alias the two.
internal sealed class FalloutNpcBodyPartTables
{
    private readonly FalloutPluginRecord _default;
    private readonly FalloutPluginRecord? _player;

    private FalloutNpcBodyPartTables(FalloutPluginRecord fallback, FalloutPluginRecord? player)
    { _default = fallback; _player = player; }

    internal static FalloutNpcBodyPartTables FromLoadedRecords(FalloutPluginStack records)
    {
        records.RequireLiveLoadReceipt(records.LoadReceipt);
        if (!records.TryGetEffective(records.RuntimeFormKey(0x1d), out var fallback) || fallback.Signature != "BPTD")
            throw new NotSupportedException("source-NPC-default-body-part-engine-record-fallback-construction-unowned");
        var player = records.TryGetEffective(records.RuntimeFormKey(0x1c), out var candidate) && candidate.Signature == "BPTD" ? candidate : null;
        return new(fallback, player);
    }

    internal FalloutPluginRecord Read(FalloutFormKey currentBase, FalloutFormKey playerBase) =>
        _player is not null && FalloutFormKeyComparer.Instance.Equals(currentBase, playerBase) ? _player : _default;
}
