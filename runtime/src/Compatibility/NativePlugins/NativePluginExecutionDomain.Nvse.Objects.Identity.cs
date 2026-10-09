using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    // Immutable publication must refuse any authoritative drift. This is not
    // a mutable engine object: a real state refresh/mutation owner must be added
    // before a changed timer, flag, field, pointer target or list can be used.
    private static string FingerprintNvseScript(NativeNvseScriptAuthority authority, NativeNvseScriptSnapshot value)
    {
        ValidateNvseScriptSnapshot(value);
        return Fingerprint(authority, writer =>
        {
            writer.Write((uint)NativeNvseSourceClass.Script); writer.Write(value.FormId); writer.Write(value.Flags);
            Bytes(writer, value.FormRuntimeBytes); Bytes(writer, value.Info); writer.Write(value.Text.HasValue);
            if (value.Text is { } text) Bytes(writer, text);
            Bytes(writer, value.Code); writer.Write(value.RuntimeWord); writer.Write(value.DelayCounter); writer.Write(value.SecondsPassed);
            ObjectIdentity(writer, value.Quest); writer.Write(value.Contributors.Count);
            foreach (var target in value.Contributors) ObjectIdentity(writer, target);
            writer.Write(value.References.Count);
            foreach (var reference in value.References)
            { Bytes(writer, reference.Name); ObjectIdentity(writer, reference.Form); writer.Write(reference.Variable); }
            writer.Write(value.Variables.Count);
            foreach (var variable in value.Variables) { Bytes(writer, variable.ScalarBytes); Bytes(writer, variable.Name); }
            Bytes(writer, value.EditorId); writer.Write(value.RuntimeFieldOwner); writer.Write(authority.CodeSha256);
        });
    }
    private static string FingerprintNvseData(NativeNvseDataAuthority authority, NativeNvseDataSnapshot value)
        => Fingerprint(authority, writer =>
        {
            var image = ComposeNvseSourceData(value);
            writer.Write((uint)value.Class); writer.Write(value.FormId); writer.Write(value.Extent); writer.Write(value.LayoutOwner);
            Bytes(writer, image); Bytes(writer, value.EditorId); writer.Write(value.Fields.Count);
            foreach (var field in value.Fields) { writer.Write(field.Offset); writer.Write(field.Owner); Bytes(writer, field.Bytes); }
            writer.Write(value.Dependencies.Count);
            foreach (var target in value.Dependencies) ObjectIdentity(writer, target);
        });
    private static string Fingerprint(NativeNvseSourceObjectAuthority authority, Action<BinaryWriter> fields)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        writer.Write(authority.SourceOwner); writer.Write(authority.SourceSha256); fields(writer); writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
    }
    private static void Bytes(BinaryWriter writer, ReadOnlyMemory<byte> bytes)
    { writer.Write(bytes.Length); writer.Write(bytes.Span); }
    private static void ObjectIdentity(BinaryWriter writer, NativeNvseSourceObject? value)
    {
        writer.Write(value is not null); if (value is null) return;
        writer.Write(value.Generation); writer.Write(value.Module); writer.Write(value.Id); writer.Write(value.Address);
        writer.Write(value.FormId); writer.Write((uint)value.Class);
    }
}
