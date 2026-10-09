using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativeCngSystemServiceContracts
{
    internal static void Run(string companion)
    {
        var image = NativePluginCngSystemBuild.ReadSibling(companion);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var provider = Path.Combine(windows, "SysWOW64", "bcrypt.dll");
        var primitives = Path.Combine(windows, "SysWOW64", "bcryptPrimitives.dll");
        var providerHash = Hash(provider); var primitivesHash = Hash(primitives);
        using var domain = new NativePluginExecutionDomain(image.Path);
        using (var prepared = Reply(domain, writer =>
        {
            writer.Write((uint)NativePluginCngServiceStep.Prepare);
            Text(writer, provider); Text(writer, providerHash); Text(writer, primitives); Text(writer, primitivesHash);
        }))
        {
            Require(prepared.ReadUInt32() == 1 && prepared.ReadUInt32() == 1, "actual Windows image admission");
            Require(ReadText(prepared) == provider && ReadText(prepared) == providerHash &&
                ReadText(prepared) == primitives && ReadText(prepared) == primitivesHash, "mapped source identities");
            Finish(prepared);
        }
        var algorithm = Call(domain, 1, 0, Encoding.Unicode.GetBytes("SHA256\0"), null, null, null).Created;
        Require(algorithm != 0, "real algorithm handle");
        var hash = Call(domain, 3, algorithm, null, null, null, null).Created;
        Require(hash != 0 && hash != algorithm, "independent actual hash handle");
        var data = Enumerable.Range(0, 140013).Select(index => unchecked((byte)(index * 37 + 11))).ToArray();
        _ = Call(domain, 4, hash, null, null, data, null);
        var result = Call(domain, 5, hash, null, null, null, Enumerable.Repeat((byte)0xcc, 32).ToArray());
        Require(result.Bytes.SequenceEqual(SHA256.HashData(data)), "complete streamed SHA256 output from Windows");
        _ = Call(domain, 6, hash, null, null, null, null);
        _ = Call(domain, 7, algorithm, null, null, null, null);
        using (var retired = Reply(domain, writer => writer.Write((uint)NativePluginCngServiceStep.Retire)))
        {
            Require(retired.ReadUInt32() == 0 && retired.ReadUInt32() == 0 && retired.ReadUInt32() == 1 &&
                retired.ReadUInt32() == 0 && retired.ReadUInt32() == 0 && retired.ReadUInt32() == 1 && retired.ReadUInt32() == 0,
                "real provider retirement with no remaining handles or buffers");
            Finish(retired);
        }
        domain.Dispose();
        Require(domain.NaturallyRetired && domain.ChildExited && domain.CngServiceProcessResourcesRetired, "exact normal service exit");
        Require(Hash(provider) == providerHash && Hash(primitives) == primitivesHash && Hash(image.Path) == image.Sha256, "unchanged input files");
        Console.WriteLine("OPENNV_CNG_SYSTEM_SERVICE_PASS streamedBytes=140013 actualWindowsSdk=true exactNormalExit=true originalModuleCalls=UNEXECUTED");
    }
    private static (ulong Created, byte[] Bytes) Call(NativePluginExecutionDomain domain, uint operation, ulong target,
        byte[]? name, byte[]? implementation, byte[]? input, byte[]? output)
    {
        byte[]?[] buffers = [name, implementation, input, output];
        ulong invocation;
        using (var begun = Reply(domain, writer =>
        {
            writer.Write((uint)NativePluginCngServiceStep.Begin); writer.Write(operation); writer.Write(target);
            writer.Write(0U); writer.Write(0U); writer.Write(0U); writer.Write(operation is 1 or 3 ? 1U : 0U);
            writer.Write(0U); writer.Write(0U);
            foreach (var buffer in buffers) { writer.Write(buffer is null ? 0U : 1U); writer.Write(checked((uint)(buffer?.Length ?? 0))); }
        })) { invocation = begun.ReadUInt64(); Finish(begun); }
        Require(invocation != 0, "real invocation identity");
        for (var role = 0; role < buffers.Length; role++)
        {
            if (buffers[role] is not { } bytes) continue;
            for (var offset = 0; offset < bytes.Length;)
            {
                var count = Math.Min(32768, bytes.Length - offset);
                using var written = Reply(domain, writer =>
                {
                    writer.Write((uint)NativePluginCngServiceStep.Write); writer.Write(invocation); writer.Write(checked((uint)role));
                    writer.Write(checked((uint)offset)); writer.Write(checked((uint)count)); writer.Write(bytes, offset, count);
                });
                offset += count; Require(written.ReadUInt32() == offset, "complete ordered source upload"); Finish(written);
            }
        }
        ulong created;
        using (var executed = Reply(domain, writer => { writer.Write((uint)NativePluginCngServiceStep.Execute); writer.Write(invocation); }))
        {
            Require(executed.ReadInt32() == 0, "actual successful SDK status"); _ = executed.ReadUInt32(); created = executed.ReadUInt64();
            Require(executed.ReadUInt32() == 0 && executed.ReadUInt32() == 0 && executed.ReadUInt32() == (output?.Length ?? 0), "typed output extent");
            Finish(executed);
        }
        byte[] returned = [];
        if (output is not null)
        {
            using var read = Reply(domain, writer =>
            { writer.Write((uint)NativePluginCngServiceStep.Read); writer.Write(invocation); writer.Write(0U); writer.Write(checked((uint)output.Length)); });
            Require(read.ReadUInt32() == output.Length, "complete Windows output read"); returned = read.ReadBytes(output.Length);
            Require(returned.Length == output.Length, "complete Windows output bytes"); Finish(read);
        }
        using var release = Reply(domain, writer => { writer.Write((uint)NativePluginCngServiceStep.Release); writer.Write(invocation); });
        Require(release.ReadUInt32() == 1, "exact completed invocation release"); Finish(release);
        return (created, returned);
    }
    private static BinaryReader Reply(NativePluginExecutionDomain domain, Action<BinaryWriter> write)
    {
        using var request = new MemoryStream(); using (var writer = new BinaryWriter(request, Encoding.UTF8, true)) write(writer);
        return new BinaryReader(new MemoryStream(domain.CngSystemExchange(request.ToArray()), false), Encoding.UTF8);
    }
    private static void Text(BinaryWriter writer, string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(checked((uint)bytes.Length)); writer.Write(bytes); }
    private static string ReadText(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(checked((int)reader.ReadUInt32())));
    private static void Finish(BinaryReader reader) => Require(reader.BaseStream.Position == reader.BaseStream.Length, "complete framed reply");
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException("CNG system service: " + reason); }
}
