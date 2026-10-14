namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifSceneRetirement
{
    internal static void Drain(string owner, IEnumerable<Action> retire)
    {
        List<Exception>? failures = null;
        foreach (var action in retire)
            try { action(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        if (failures is not null) throw new AggregateException(owner + " retained independent retirement failures.", failures);
    }
}
