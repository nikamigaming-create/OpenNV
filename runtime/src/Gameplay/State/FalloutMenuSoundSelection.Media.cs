using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutMenuSoundSelection
{
    internal string RequirePreparedMediaPath(long ordinal, string logicalPath)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retired, this);
            var row = Attempt(ordinal);
            if (row.Phase != FalloutMenuSoundSelectionPhase.Selected || row.SelectedPath != logicalPath)
                throw new InvalidDataException("Menu media read lost its real successful source selection ordinal/path.");
            RequireWinningSource(row);
            var raw = FalloutMenuSoundSelectionSource.PreparedPath(row.RawPath);
            if (IsDirectory(row.RawPath)) raw += logicalPath[(logicalPath.LastIndexOf('\\') + 1)..];
            if (FalloutMenuSoundSelectionSource.LogicalPath(raw) != logicalPath)
                throw new InvalidDataException("Original prepared menu read differs from its immutable selected source path.");
            return raw;
        }
    }
}
