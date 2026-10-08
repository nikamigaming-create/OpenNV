using System.Buffers.Binary;
using System.Diagnostics;
using OpenNV.Runtime.Compatibility.NativePlugins;

// Deliberately malformed traffic reaches the real companion's public pipe
// parser. These are test children, not a second gameplay/audit implementation.
internal static class NativeExecutionProtocolContracts
{
    internal static void Run(string companion)
    {
        Fault(companion, "generation", header => BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), 901), 13);
        Fault(companion, "parent", header => BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), 1), 13);
        Fault(companion, "reserved", header => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), 1), 13);
        Fault(companion, "payload-budget", header => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), NativePluginExecutionDomain.MaximumPayload + 1U), 13);
        Fault(companion, "message-kind", header => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 99), 13);
        Fault(companion, "operation", header => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 99), 1);
        Fault(companion, "truncated-payload", header => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), 8), 109, truncated: true);
        Fault(companion, "duplicate-call", _ => { }, 13, duplicate: true);
    }
    private static void Fault(string companion, string name, Action<byte[]> corrupt, uint expectedCode, bool truncated = false, bool duplicate = false)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(companion))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--generation"); start.ArgumentList.Add("900");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Native protocol child did not start.");
        var diagnostics = process.StandardError.ReadToEndAsync();
        try
        {
            var header = Header();
            if (duplicate)
            {
                process.StandardInput.BaseStream.Write(header); process.StandardInput.BaseStream.Flush();
                var hello = ReadFrame(process);
                Require(BinaryPrimitives.ReadUInt32LittleEndian(hello.Header.AsSpan(8)) == (uint)NativePluginDomainMessage.Reply,
                    "Sequence fixture did not first admit a real native hello.");
            }
            corrupt(header); process.StandardInput.BaseStream.Write(header);
            if (truncated) { process.StandardInput.BaseStream.Write(new byte[4]); process.StandardInput.Close(); }
            else process.StandardInput.BaseStream.Flush();
            var failure = ReadFrame(process);
            Require(BinaryPrimitives.ReadUInt64LittleEndian(failure.Header.AsSpan(16)) == 900 &&
                BinaryPrimitives.ReadUInt32LittleEndian(failure.Header.AsSpan(8)) == (uint)NativePluginDomainMessage.Fault &&
                failure.Payload.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(failure.Payload) == expectedCode,
                $"Malformed {name} did not fail under its actual native parser owner.");
            Require(process.WaitForExit(3000) && process.ExitCode == 3, "Native protocol failure did not terminate its own generation.");
            Require(diagnostics.Wait(TimeSpan.FromSeconds(1)) && diagnostics.Result.Contains("OPENNV_NATIVE_DOMAIN_FAULT", StringComparison.Ordinal),
                "Native protocol fault lost its diagnostic footer.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: false);
            Require(process.WaitForExit(3000), "Owned native protocol child is still alive.");
        }
    }
    private static byte[] Header()
    {
        var header = new byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(header, NativePluginExecutionDomain.ProtocolMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), NativePluginExecutionDomain.ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)NativePluginDomainMessage.Request);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)NativePluginDomainOperation.Hello);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), 900);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24), 1);
        return header;
    }
    private static (byte[] Header, byte[] Payload) ReadFrame(Process process)
    {
        var header = new byte[48];
        process.StandardOutput.BaseStream.ReadExactlyAsync(header).AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        Require(BinaryPrimitives.ReadUInt32LittleEndian(header) == NativePluginExecutionDomain.ProtocolMagic &&
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) == NativePluginExecutionDomain.ProtocolVersion &&
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(44)) == 0, "Native protocol fault envelope drifted.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(40));
        Require(count <= NativePluginExecutionDomain.MaximumPayload, "Native protocol fault exceeds its budget.");
        var payload = new byte[checked((int)count)];
        if (payload.Length != 0)
            process.StandardOutput.BaseStream.ReadExactlyAsync(payload).AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        return (header, payload);
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }
}
