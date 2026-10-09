namespace OpenNV.Runtime.Content;

internal static partial class NativeOwnedMediaLoader
{
    private static byte[] ReadSourceMenu(string logicalPath, string? preferredArchive, long selectionOrdinal, out string source)
    {
        if (preferredArchive is not null)
            throw new NotSupportedException("Original indexed/menu reader has no archive-hint override consumer.");
        var owned = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Actual menu media requires its living selected owned source.");
        if (!owned.TryReadSourceMenuAudio(logicalPath, selectionOrdinal, out var payload, out source))
            throw new FileNotFoundException("Actual source menu media is missing.", logicalPath);
        return payload;
    }
}
