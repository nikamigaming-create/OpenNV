namespace OpenNV.Runtime.Content;

internal enum NativeGame
{
    Fallout1,
    Fallout2,
    Fallout3,
    FalloutNewVegas,
}

internal sealed record NativeGameInstallation(NativeGame Game, string InstallRoot, string ContentRoot)
{
    private const string FalloutNewVegasMasterName = "FalloutNV" + ".esm";

    internal static NativeGameInstallation Detect(string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot))
            throw new ArgumentException("A game installation root is required.", nameof(selectedRoot));

        var root = Path.GetFullPath(selectedRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Selected game folder does not exist: {root}");

        var dataRoot = FindDirectory(root, "Data");
        var contentRoot = dataRoot ?? root;
        var game = DetectGame(root, contentRoot);
        return new NativeGameInstallation(game, root, contentRoot);
    }

    private static NativeGame DetectGame(string installRoot, string contentRoot)
    {
        if (ContainsFile(installRoot, "patch000.dat") &&
            ContainsFile(installRoot, "critter.dat") &&
            ContainsFile(installRoot, "master.dat"))
            return NativeGame.Fallout2;
        if (ContainsFile(installRoot, "critter.dat") && ContainsFile(installRoot, "master.dat"))
            return NativeGame.Fallout1;
        if (ContainsFile(contentRoot, "Fallout3.esm"))
            return NativeGame.Fallout3;
        if (ContainsFile(contentRoot, FalloutNewVegasMasterName))
            return NativeGame.FalloutNewVegas;
        throw new InvalidDataException(
            "The selected folder is not a recognized Fallout 1, Fallout 2, Fallout 3, or Fallout: New Vegas installation.");
    }

    private static string? FindDirectory(string root, string name) =>
        Directory.EnumerateDirectories(root)
            .SingleOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsFile(string root, string name) =>
        Directory.EnumerateFiles(root)
            .Any(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));
}
