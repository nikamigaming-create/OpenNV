using System.Buffers.Binary;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

// Public xNVSE expression argument transport, independent of vanilla
// ExtractArgsEx. A single source leaf retains its lvalue. Operators, commands,
// globals, lambdas and foreign event lists need their own reached owners.
internal static class FalloutNativePluginExpressionStream
{
    internal static NativeNvseExpressionArgumentOwner Bind(FalloutNativePluginSourceCall call,
        NativeNvseLocalContext context)
    {
        var source = call.Program.Scope.Single(field => field.Signature == "SCDA").Data;
        if (call.Start > call.End || call.End > source.Length || context.Script is null ||
            context.Script.Authority is not NativeNvseScriptAuthority script ||
            !StringComparer.OrdinalIgnoreCase.Equals(script.CodeSha256, context.Authority.CodeSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(script.SourceSha256, call.Program.Scope.RecordSha256))
            throw new InvalidDataException("Native expression stream lacks its exact original SCDA/Script/event-list join.");
        var payload = source.Slice(checked((int)call.Start), checked((int)(call.End - call.Start)));
        var owner = call.Program.Scope.ScopeSha256 + ":" + call.Start + ":" + call.End;
        return new(owner, call.Start, call.End,
            () => new(call.End, call.Evaluate().Select(FalloutNativePluginExpressionArguments.Convert).ToArray()),
            () => Read(payload, call.Start, (type, reference, slot) =>
                NativeNvseExpressionValue.Variable(NativeNvseExpressionLocal.Bind(context, type, reference, slot))));
    }

    internal static NativeNvseEvaluatedArguments Read(ReadOnlyMemory<byte> payload, uint start,
        Func<byte, ushort, ushort, NativeNvseExpressionValue> local)
    {
        ArgumentNullException.ThrowIfNull(local);
        var cursor = new FalloutCompiledOperandCursor(payload);
        var count = cursor.Byte(); var values = new NativeNvseExpressionValue[count];
        for (var index = 0; index < count; ++index)
        {
            var extent = cursor.UInt16();
            if (extent < sizeof(ushort)) throw new InvalidDataException("NVSE argument expression extent omits its own length.");
            var leaf = new FalloutCompiledOperandCursor(cursor.Bytes(extent - sizeof(ushort)));
            values[index] = leaf.Byte() switch
            {
                (byte)'B' or (byte)'b' => NativeNvseExpressionValue.Numeric(leaf.Byte()),
                (byte)'I' or (byte)'i' => NativeNvseExpressionValue.Numeric(leaf.UInt16()),
                (byte)'L' or (byte)'l' => NativeNvseExpressionValue.Numeric(BinaryPrimitives.ReadUInt32LittleEndian(leaf.Bytes(4).Span)),
                (byte)'Z' => NativeNvseExpressionValue.Numeric(leaf.Double()),
                (byte)'S' => String(leaf),
                (byte)'V' => local(leaf.Byte(), leaf.UInt16(), leaf.UInt16()),
                _ => throw new NotSupportedException("NVSE argument token requires its source operator/command/global/reference/lambda owner."),
            };
            if (!leaf.AtEnd)
                throw new NotSupportedException("NVSE argument expression contains additional unowned token/operator behavior.");
        }
        cursor.RequireEnd();
        // The public evaluator moves its source cursor past the count byte,
        // but its external opcode offset advances by each expression extent.
        // Keep those coordinates independent; zero arguments leave the offset.
        return new(checked(start + (uint)(payload.Length - 1)), values);
    }

    private static NativeNvseExpressionValue String(FalloutCompiledOperandCursor cursor)
        => NativeNvseExpressionValue.String(cursor.Bytes(cursor.UInt16()).Span);
}
