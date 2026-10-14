namespace OpenNV.Runtime.Content;

internal sealed record FalloutCommandInputPermission(string Source, Guid Factory, FalloutInputProvider Provider,
    ulong Window, ulong Sample, long Revision, uint Key, bool RawDown, FalloutDirectInputKey Controls, bool Allowed);

internal sealed partial class FalloutDirectInputState
{
    // Command/UI adaptation may inspect the current mutable control filter
    // before the next normal device sample. It never publishes an engine byte
    // or DIHook cache, consumes tap, shifts previous state or emits script edges.
    internal FalloutCommandInputPermission ReadCommandEventPermission(uint key, bool rawDown)
    {
        RequireOwner(); RequireIdleOriginal();
        if (Provider != FalloutInputProvider.HeadlessCommands || Window != 0)
            throw new InvalidOperationException("Command event permission requires its exact command provider.");
        if (key >= KeyCount) throw new NotSupportedException("Command event permission has no actual keyboard/mouse key: " + key);
        var fields = _keys[key]; var (game, _) = ReadHookFilter(fields, rawDown);
        return new(Source, Factory, Provider, Window, Sample, Revision, key, rawDown, fields, !rawDown || game);
    }
}
