namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>The source interpreter folds ASCII letters only.</summary>
internal static class ClassicIntExportNames
{
    internal static bool Equal(string left, string right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
            if (Fold(left[index]) != Fold(right[index])) return false;
        return true;
    }

    private static char Fold(char value) => value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) : value;
}
