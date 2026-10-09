namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutAdvancementRuntimeSource
{
    internal void RequireLivingRestSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Receipt.Validate();
    }
}
