using System.Runtime.InteropServices;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static partial class NativeMoveFileContracts
{
    internal static void RunPolicy(string authoredFixture)
    {
        authoredFixture = Path.GetFullPath(authoredFixture);
        var moduleHash = Hash(authoredFixture);
        var temporary = Directory.CreateTempSubdirectory("opennv-move-policy-").FullName;
        try
        {
            var originalRoot = Directory.CreateDirectory(Path.Combine(temporary, "source")).FullName;
            var first = Path.Combine(originalRoot, "first.cfg"); var second = Path.Combine(originalRoot, "second.cfg");
            var final = Path.Combine(originalRoot, "final.cfg"); var asset = Path.Combine(originalRoot, "authored-asset.bin");
            File.WriteAllText(first, "FIRST", new UTF8Encoding(false)); File.WriteAllText(second, "SECOND", new UTF8Encoding(false));
            File.WriteAllText(asset, "AUTHORED READ-ONLY ASSET", new UTF8Encoding(false));
            var originalHashes = new[] { first, second, asset }.ToDictionary(path => path, Hash);
            var selection = new NativePluginIoSelection(moduleHash, moduleHash, authoredFixture, originalRoot,
                Path.Combine(temporary, "state"), [originalRoot, Path.GetDirectoryName(authoredFixture)!],
                new[] { first, second, final, asset }.Select(path => new NativePluginIoWriteScope(path, false,
                    NativePluginIoRole.Configuration, "authored-private-move-policy")).ToArray(),
                path => originalHashes.TryGetValue(path, out var hash) ? new(path, hash, "authored-private-move-winner", path != asset) : null,
                "authored-private-move-policy-imports", new HashSet<string>(StringComparer.Ordinal)) { CrtProviders = [] };
            using (var io = new NativePluginPrivateIo(selection))
            {
                io.Claim(41);
                Reject<NotSupportedException>(() => io.ResolveMove(17, first, Path.Combine(originalRoot, "undeclared.cfg"), 0));
                Reject<NotSupportedException>(() => io.ResolveMove(17, first, second, 4));
                Reject<NotSupportedException>(() => io.ResolveMove(17, asset, second, 1));
                Require(io.Receipts.Count == 0, "complete pair preflight before source copies and route publication");

                var conflict = io.ResolveMove(17, first, second, 0);
                var returned = MoveFileEx(conflict.Source.PhysicalPath!, conflict.Destination.PhysicalPath!, 0);
                var error = unchecked((uint)Marshal.GetLastWin32Error());
                Require(!returned && error is 80 or 183, "actual Windows existing-file failure");
                Reject<InvalidDataException>(() => io.CompleteMove(17, conflict.Source.Id, conflict.Destination.Id, 1, returned, error));
                io.CompleteMove(17, conflict.Source.Id, conflict.Destination.Id, 0, returned, error);
                Require(File.ReadAllText(conflict.Source.PhysicalPath!) == "FIRST" && File.ReadAllText(conflict.Destination.PhysicalPath!) == "SECOND",
                    "failed native move preserves both actual files");

                var replace = io.ResolveMove(17, first, second, 9);
                returned = MoveFileEx(replace.Source.PhysicalPath!, replace.Destination.PhysicalPath!, 9);
                error = unchecked((uint)Marshal.GetLastWin32Error()); Require(returned, "real replace/write-through");
                io.CompleteMove(17, replace.Source.Id, replace.Destination.Id, 9, returned, error);
                Reject<InvalidDataException>(() => io.CompleteMove(17, replace.Source.Id, replace.Destination.Id, 9, returned, error));
                var rename = io.ResolveMove(17, second, final, 8);
                returned = MoveFileEx(rename.Source.PhysicalPath!, rename.Destination.PhysicalPath!, 8);
                error = unchecked((uint)Marshal.GetLastWin32Error()); Require(returned, "real rename/write-through");
                io.CompleteMove(17, rename.Source.Id, rename.Destination.Id, 8, returned, error);
                Require(io.MoveReceipts.Count == 3 && io.MoveReceipts[1].SourceRemoved && io.MoveReceipts[2].SourceRemoved,
                    "one returned receipt per actual paired move");
                io.RequireRetired();
            }
            using (var cold = new NativePluginPrivateIo(selection))
            {
                cold.Claim(42);
                foreach (var path in new[] { first, second })
                {
                    var absent = cold.Resolve(18, 7, NativePluginIoAction.Attributes, path);
                    Require(absent.Error == 2, "cold rename tombstone suppresses original source winner");
                    cold.Complete(18, absent.Id, 7, absent.Error);
                }
                var destination = cold.Resolve(18, 7, NativePluginIoAction.Attributes, final);
                Require(destination.Error == 0 && File.ReadAllText(destination.PhysicalPath!) == "FIRST", "cold destination uses actual retained private file");
                cold.Complete(18, destination.Id, 7, 0); cold.RequireRetired();
            }
            foreach (var (path, hash) in originalHashes) Require(Hash(path) == hash, "original input unchanged: " + Path.GetFileName(path));
            Require(Hash(authoredFixture) == moduleHash, "authored native module unchanged");
            Console.WriteLine("OPENNV_NATIVE_MOVE_POLICY_PASS actualWindows=true pairedPreflight=true failureAtomic=true wrongPairRefused=true " +
                "replayRefused=true originals=unchanged assetWrites=refused coldNamespace=true gameSupport=unaccepted");
        }
        finally
        {
            var absolute = Path.GetFullPath(temporary);
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(absolute).StartsWith("opennv-move-policy-", StringComparison.Ordinal))
                throw new InvalidDataException("Move-policy cleanup escaped its generated workspace.");
            Directory.Delete(absolute, true);
        }
    }
    [DllImport("kernel32", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string destination, uint flags);
}
