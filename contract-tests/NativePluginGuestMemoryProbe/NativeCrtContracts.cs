using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeCrtContracts
{
    internal static void Run(string companion, string fixture, string runtime, string nvse)
    {
        Require(OperatingSystem.IsWindows() && Environment.Is64BitProcess,
            "CRT execution requires the actual Windows x64-managed/x86-native boundary.");
        fixture = Path.GetFullPath(fixture); runtime = Path.GetFullPath(runtime); nvse = Path.GetFullPath(nvse);
        var originals = new[] { Path.GetFullPath(companion), fixture, runtime, nvse }.ToDictionary(path => path, Hash);
        var providers = NativePluginCrtImports.ReadProviders(fixture, originals[fixture]);
        Require(providers.Count == 1, "The authored /MD fixture has no unique actual Windows CRT provider.");
        foreach (var provider in providers) originals.Add(provider.Path, Hash(provider.Path));
        var temporary = Directory.CreateTempSubdirectory("opennv-actual-crt-").FullName;
        var sourceRoot = Directory.CreateDirectory(Path.Combine(temporary, "authored-source")).FullName;
        var input = Path.Combine(sourceRoot, "source.cfg"); File.WriteAllText(input, "ABCDE", new UTF8Encoding(false));
        originals.Add(input, Hash(input));
        var runtimeDirectory = Path.GetDirectoryName(runtime)!;
        var virtualInput = Path.Combine(runtimeDirectory, "OpenNVCrtAuthored.cfg");
        var virtualOutput = Path.Combine(runtimeDirectory, "OpenNVCrtAuthored.out");
        var virtualDirectory = Path.Combine(runtimeDirectory, "OpenNVCrtAuthored-dir");
        var selection = new NativePluginIoSelection(HashText("authored-public-ABI-CRT\0" + originals[fixture]),
            originals[fixture], fixture, runtimeDirectory, Path.Combine(temporary, "private-state"),
            [runtimeDirectory, Path.GetDirectoryName(nvse)!, Path.GetDirectoryName(fixture)!, sourceRoot],
            [new(virtualInput, false, NativePluginIoRole.Configuration, "authored-current-configuration"),
                new(virtualOutput, false, NativePluginIoRole.Diagnostic, "authored-native-output"),
                new(virtualDirectory, true, NativePluginIoRole.State, "authored-native-directory")],
            path => StringComparer.OrdinalIgnoreCase.Equals(path, virtualInput)
                ? new(input, originals[input], "authored-five-byte-winning-input", true) : null,
            "authored-public-ABI-CRT-imports",
            new HashSet<string>(StringComparer.Ordinal)
            {
                "api-ms-win-crt-runtime-l1-1-0.dll!_get_errno",
                "api-ms-win-crt-runtime-l1-1-0.dll!_get_doserrno",
            });
        try
        {
            ProtectAuthoredSource(sourceRoot, input);
            using var host = NativeNvseHostSource.Open(runtime, originals[runtime], nvse, originals[nvse],
                selection.StackSha256, false, "authored-host-selected-owned-standard-edition");
            using var io = new NativePluginPrivateIo(selection);
            Require(io.CrtProviders.Count == 1 && io.CrtProviders[0].Sha256 == providers[0].Sha256,
                "Actual provider was replaced before native admission.");
            using var domain = new NativePluginExecutionDomain(companion, TimeSpan.FromSeconds(30), privateIo: io);
            Require(domain.NativeThread != 0 && domain.ObjectSecurity is { } objectSecurity &&
                objectSecurity.RestrictingSid == io.RestrictingSid,
                "Actual CRT caller has no restricted child/default-object source receipt.");
            try
            {
                var plugin = domain.LoadNvseImage(host, fixture, originals[fixture]);
                var query = domain.QueryNvse(plugin);
                Require(query.Returned, "Actual authored Query refused; retain its native CRT prefix.");
                var load = domain.InitializeNvse(plugin);
                Require(load.Returned, "Actual authored Load refused; retain its native CRT prefix.");
                var receipts = domain.NvseCrtReceipts.ToArray();
                Require(receipts.Length != 0 && receipts.All(row => row.Generation == domain.Generation &&
                    row.Parent != 0 && row.Provider != 0 && row.Route != 0 && row.DeclarationOwner == providers[0].SourceOwner),
                    "Actual CRT result lost its selected provider/generation/callback owner.");
                var opened = receipts.Where(row => row.Operation == NativePluginCrtOperation.Open && row.Stream != 0).ToArray();
                var closed = receipts.Where(row => row.Operation == NativePluginCrtOperation.Close).ToArray();
                Require(opened.Length == 6 && closed.Length == opened.Length && closed.All(row => row.StreamRetired && row.Result == 0) &&
                    opened.All(row => closed.Count(close => close.Route == row.Route && close.Stream == row.Stream) == 1),
                    "An actual opaque FILE lifetime was missing, aliased or not retired exactly once.");
                Require(receipts.Any(row => row.Operation == NativePluginCrtOperation.Read && row.Argument == 2 &&
                    row.Requested == 4 && row.Result == 2 && row.Status.EndOfFile != 0),
                    "Actual partial-element read did not retain its complete-element/EOF result.");
                Require(receipts.Count(row => row.Operation == NativePluginCrtOperation.Open && row.Stream == 0 &&
                    row.Status.Errno != 0 && row.Status.DosError != 0 && !row.Status.StreamStatusAvailable) == 2,
                    "Source absence or tombstone lost the actual failed CRT open/status.");
                foreach (var operation in new[] { NativePluginCrtOperation.Seek, NativePluginCrtOperation.Tell,
                    NativePluginCrtOperation.Rewind, NativePluginCrtOperation.Flush, NativePluginCrtOperation.PutCharacter,
                    NativePluginCrtOperation.PutString, NativePluginCrtOperation.FormattedWrite,
                    NativePluginCrtOperation.EndOfFile, NativePluginCrtOperation.Error, NativePluginCrtOperation.ClearError })
                    Require(receipts.Any(row => row.Operation == operation), "Actual CRT operation was not reached: " + operation);
                Require(receipts.Count(row => row.Operation == NativePluginCrtOperation.MakeDirectory && row.Result == 0) == 2,
                    "Actual narrow/wide directory creation was not published.");
                var output = io.Receipts.FirstOrDefault(row => row.VirtualPath == virtualOutput &&
                    row.PhysicalPath is not null && row.Role == NativePluginIoRole.Diagnostic)?.PhysicalPath ??
                    throw new InvalidDataException("Actual output has no private destination receipt.");
                Require(File.ReadAllBytes(output).AsSpan().SequenceEqual(Encoding.ASCII.GetBytes("XAYA\nline\nformat=tag:7:6.25\ntail")),
                    "Native buffer/variadic/seek/append output differs from the independent expected bytes.");
                Require(!File.Exists(virtualOutput) && !Directory.Exists(virtualDirectory),
                    "Authored native I/O escaped its selected private state into the owned runtime.");
                _ = domain.UnloadNvse(plugin);
                domain.Dispose();
                Require(domain.ChildExited && domain.NaturallyRetired && domain.Fault is null,
                    "The actual native child did not retire cleanly before source/provider cleanup.");
                Console.WriteLine("OPENNV_NATIVE_CRT_CONTRACT_PASS authoredPublicNvse=true restrictedChild=true actualProvider=true " +
                    "opaqueFile=true nativeBuffers=true partialElements=true variadic=true sourceWinner=true " +
                    "privateConfiguration=true tombstone=true errno=true retirement=true sourceBytes=unchanged " +
                    "originalDllCompatibility=unaccepted profileDirectoryExecution=unexecuted");
            }
            catch
            {
                foreach (var row in io.CrtReceipts)
                    Console.Error.WriteLine($"OPENNV_NATIVE_CRT_PREFIX sequence={row.Sequence} operation={row.Operation} " +
                        $"argument={row.Argument} requested={row.Requested} result={row.Result} errno={row.Status.Errno} " +
                        $"dos={row.Status.DosError} eof={row.Status.EndOfFile} retired={row.StreamRetired}");
                throw;
            }
        }
        catch (NativePluginDomainFaultException error)
        {
            Console.Error.WriteLine($"OPENNV_NATIVE_CRT_DOMAIN_FAILURE childExited={error.Owner.ChildExited} " +
                $"childExit={error.Owner.ChildExitCode} nativeCode={error.Fault.NativeCode} diagnostics={error.Owner.Fault?.Diagnostics} " +
                $"defaultObjects={error.Owner.ObjectSecurity}");
            throw;
        }
        finally
        {
            try
            {
                foreach (var (path, digest) in originals) Require(Hash(path) == digest, "CRT execution changed a readonly input: " + path);
            }
            finally
            {
                // This exact freshly created test root owns every generated file.
                var absolute = Path.GetFullPath(temporary);
                var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(absolute).StartsWith("opennv-actual-crt-", StringComparison.Ordinal))
                    throw new InvalidDataException("Authored CRT cleanup escaped its exact temporary workspace.");
                Directory.Delete(absolute, true);
            }
        }
    }

    private static string Hash(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input));
    }
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void ProtectAuthoredSource(string directory, string input)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User?.Value ?? throw new InvalidDataException("Authored source has no actual creator SID.");
        // OWNER RIGHTS makes the original creator's implicit WRITE_DAC explicit.
        // The normal test owner keeps cleanup rights; the write-restricted native
        // token has no source write ACE. This touches authored scratch files only.
        var sddl = "D:P(D;OICI;0x40;;;" + user + ")(A;OICI;FA;;;SY)(A;OICI;RC;;;OW)(A;OICI;FA;;;" + user + ")";
        var directorySecurity = new DirectorySecurity(); directorySecurity.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
        new DirectoryInfo(directory).SetAccessControl(directorySecurity);
        var fileSecurity = new FileSecurity(); fileSecurity.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
        new FileInfo(input).SetAccessControl(fileSecurity);
    }
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
}
