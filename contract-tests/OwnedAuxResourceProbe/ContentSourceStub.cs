namespace OpenNV.Runtime.Content;

// The format probe supplies synthetic bytes directly; no installation is used.
internal sealed class RuntimeLiveContentSource
{
    internal bool TryRead(string logicalPath, string? preferredArchive, out byte[] data, out string source)
    {
        data = [];
        source = string.Empty;
        return false;
    }
}
