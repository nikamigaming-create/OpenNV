using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Values are produced by an authoritative C# caller. These are not native
// TESForm/Script/ArrayVar layouts and cannot be dereferenced as those objects.
internal enum NativeNvseTokenType : uint { Number = 0, Boolean = 1, String = 2, Form = 3, Array = 6, Pair = 21 }
internal sealed record NativeNvseExpressionValue
{
    internal NativeNvseTokenType Type { get; }
    internal double Number { get; }
    internal ImmutableArray<byte> Text { get; }
    internal NativeNvseExpressionValue? Left { get; }
    internal NativeNvseExpressionValue? Right { get; }

    private NativeNvseExpressionValue(NativeNvseTokenType type, double number, ImmutableArray<byte> text,
        NativeNvseExpressionValue? left = null, NativeNvseExpressionValue? right = null)
    { Type = type; Number = number; Text = text; Left = left; Right = right; }

    internal static NativeNvseExpressionValue Numeric(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Native expression number is non-finite.");
        return new(NativeNvseTokenType.Number, value, []);
    }
    internal static NativeNvseExpressionValue Boolean(bool value) => new(NativeNvseTokenType.Boolean, value ? 1 : 0, []);
    internal static NativeNvseExpressionValue String(ReadOnlySpan<byte> value)
    {
        if (value.Length > 16384 || value.Contains((byte)0)) throw new InvalidDataException("Native expression string extent is invalid.");
        return new(NativeNvseTokenType.String, 0, ImmutableArray.CreateRange(value.ToArray()));
    }
    internal static NativeNvseExpressionValue Identity(NativeNvseTokenType type, uint id)
    {
        if (type is not (NativeNvseTokenType.Form or NativeNvseTokenType.Array)) throw new ArgumentOutOfRangeException(nameof(type));
        return new(type, id, []);
    }
    internal static NativeNvseExpressionValue Pair(NativeNvseExpressionValue left, NativeNvseExpressionValue right)
    {
        ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
        if (left.Type is not (NativeNvseTokenType.Number or NativeNvseTokenType.String) || right.Type == NativeNvseTokenType.Pair)
            throw new InvalidDataException("Native transient pair needs a numeric/string key and basic value.");
        return new(NativeNvseTokenType.Pair, 0, [], left, right);
    }
}

// The public Init API supplies no size/version parameter. Its writable extent
// and field-11 meaning must come from the exact selected client's declaration.
// A declaration is an admission input, not proof that the DLL uses no other ABI.
internal sealed record NativeNvseExpressionAbi(string PluginSha256, string DeclarationOwner,
    uint DeclaredBytes, uint CallableBytes, bool ArrayAccessorReturnsNativePointer);
internal sealed record NativeNvseEvaluatedArguments(uint EndOffset, IReadOnlyList<NativeNvseExpressionValue> Values);
internal sealed record NativeNvseExpressionArgumentOwner(string SourceOwner, uint StartOffset, uint MaximumEndOffset,
    Func<NativeNvseEvaluatedArguments> Evaluate);
internal sealed record NativeNvseExpressionCallerReceipt(ulong Caller, uint Opcode, bool Returned, uint RawEax,
    double NumericResult, uint EndOffset, uint EvaluatorsCreated, uint EvaluatorsDestroyed, uint Tokens,
    int StackDelta, uint PreservedRegisters, uint ExceptionCode)
{
    internal NativeNvseElementValue? TypedResult { get; init; }
    internal uint? PublishedIdentity { get; init; }
}
internal readonly record struct NativeNvseExpressionStatistics(uint Initializations, uint Created, uint Destroyed,
    uint Active, uint LivePages, uint RetiredPages, uint CommittedBytes, uint ReservedBytes);

internal sealed class NativeNvseExpressionCaller(ulong id, NativeNvseCommand command,
    NativePluginGuestAllocation scriptData, NativeNvseExpressionArgumentOwner arguments)
{
    internal ulong Id { get; } = id;
    internal NativeNvseCommand Command { get; } = command;
    internal NativePluginGuestAllocation ScriptData { get; } = scriptData;
    internal NativeNvseExpressionArgumentOwner Arguments { get; } = arguments;
    internal Dictionary<ulong, (uint Pointer, bool Extracted)> Evaluators { get; } = [];
    internal uint Created { get; set; }
    internal uint Destroyed { get; set; }
    internal uint Tokens { get; set; }
    internal uint? NativeParameters { get; set; }
    internal uint? NativeResult { get; set; }
    internal uint? NativeOffset { get; set; }
    internal NativeNvseLocalContext? LocalContext { get; init; }
    internal uint NativeEventList => LocalContext?.EventList ?? 0;
    internal NativeNvseSourceObject? SourceScript { get; init; }
    internal IReadOnlyList<NativeNvseSourceObject> SourceObjects { get; init; } = [];
    internal uint NativeScript => SourceScript?.Address ?? 0;
    internal NativeNvseValueResultTarget? ResultTarget { get; set; }
    internal NativeNvseElementValue? PublishedValue { get; set; }
    internal uint? PublishedIdentity { get; set; }
    internal byte ExpectedReturn { get; set; }
}
