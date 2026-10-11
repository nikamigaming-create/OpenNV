using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal sealed record SourceImageOptionFields(uint Strength, uint First, uint Second, uint Third,
    uint Mode, byte[] Enabled, int Length, int Count, byte Dirty);
internal sealed record SourceImageDofFields(uint Strength, uint? First, uint? Second,
    uint? Focus, uint? Range, uint? Lower, uint? Upper, uint? Mode,
    bool? FirstEnabled, bool? SecondEnabled, byte Dirty, uint?[] Parameters);
internal sealed record SourceImageRadialFields(uint Strength, uint[] Parameters, uint[] Center, byte Dirty);
internal sealed record SourceImageNativeFields(FalloutSourceImageSpaceOutput Output,
    SourceImageOptionFields Options, SourceImageDofFields Depth, SourceImageRadialFields Radial,
    uint Scalar, byte ScalarDirty, uint[] Globals, IReadOnlyList<SourceImageFieldWrite> Writes);
internal sealed record SourceImageFieldWrite(string Field, uint? Value, int? Count = null, bool? Executed = true);

internal partial class RetailHdrCompositorEffect
{
    private readonly FalloutSourceImageSpaceAllocation _sourceAllocation = new();
    private readonly List<object> _sourceAllocations = [];
    private SourceImageNativeFields? _sourceNativeFields;
    private uint _sourceBlur, _sourceDoubleVision;
    private uint?[]? _sourceOptions;
    private byte[]? _sourceOptionBytes;
    private int _sourceOptionCount;
    private uint?[]? _sourceDepth;
    private uint?[]? _sourceRadial;
    private uint?[]? _sourceScalar;
    private uint? _sourceDofMode, _sourceDofRange;
    private byte _sourceDofDirty;
    private bool _sourceReceiverConstructionEntered, _sourceReceiverFactory, _sourceReceiverRetired;
    internal SourceImageNativeFields? SourceNativeFields => _sourceNativeFields;
    private Exception? _sourceDrawFailure;
    internal string? SourceDrawFailure => Volatile.Read(ref _sourceDrawFailure)?.ToString();

    internal void RetainSourceDrawFailure(Exception failure)
    {
        if (_sourceReceiverFactory) Interlocked.CompareExchange(ref _sourceDrawFailure, failure, null);
    }

    internal void SetSourceMainFrame(FalloutImageSpaceFrame frame, float delta)
    {
        RequireSourceReceiverFactory();
        var fields = _sourceNativeFields ?? throw new InvalidOperationException("Source Main did not write native image fields.");
        if (fields.Globals.Length != 5)
            throw new InvalidOperationException("Source Main has not completed the ordered native profile/global child.");
        static Godot.Vector4 Vector(System.Numerics.Vector4 value) => new(value.X, value.Y, value.Z, value.W);
        // These are setters, not draw predicates. The exact immutable output
        // travels with the render parameters and owns all passes/both eyes.
        Volatile.Write(ref _parameters, new(frame.TargetLuminance, Vector(frame.Cinematic),
            Vector(frame.Tint), Vector(frame.Fade), delta, frame.BlurRadius,
            new(frame.DoubleVisionOffset.X, frame.DoubleVisionOffset.Y), fields));
    }

    internal IDisposable EnterSourceAllocationCategory()
    {
        RequireSourceReceiverFactory();
        return _sourceAllocation.Enter();
    }

    // These objects own actual parameter arrays retained by this compositor.
    // The renderer consumes their immutable publication; no saved native ID
    // or previous process supplies a current receiver/resource association.
    internal void ConstructSourceReceiverFields(FalloutSourceImageSpaceDofConstructor dof)
    {
        if (_sourceReceiverConstructionEntered || _sourceReceiverFactory || _sourceReceiverRetired)
            throw new InvalidOperationException("Source image receiver factory cannot replay or replace living fields.");
        if (!Godot.GodotObject.IsInstanceValid(this) || Volatile.Read(ref _released) != 0 || _sourceEffects is null)
            throw new InvalidOperationException("Source image factory has no living compositor/program resource.");
        dof.Validate();
        _sourceReceiverConstructionEntered = true;
        T Allocate<T>(Func<T> constructor) where T : class
        {
            var allocation = _sourceAllocation.Allocate(constructor);
            _sourceAllocations.Add(allocation);
            return allocation;
        }
        using var scope = _sourceAllocation.Enter();
        _sourceOptionBytes = Allocate(() => new byte[] { 1, 1, 1, 0 });
        _sourceOptionCount = 3;
        _sourceOptions = Allocate(() => new uint?[6]); _sourceOptions[5] = 1;
        _sourceDepth = Allocate(() => dof.Parameters.Select(value => (uint?)value).ToArray());
        _sourceDofMode = dof.Mode; _sourceDofRange = dof.FocusRange; _sourceDofDirty = 1;
        _sourceRadial = Allocate(() => new uint?[8]);
        _sourceRadial[5] = _sourceRadial[6] = BitConverter.SingleToUInt32Bits(.5f); _sourceRadial[7] = 1;
        _sourceScalar = Allocate(() => new uint?[2]); _sourceScalar[0] = 0; _sourceScalar[1] = 1;
        _sourceReceiverFactory = true;
    }

    internal void RequireSourceReceiverFactory()
    {
        if (!_sourceReceiverFactory || _sourceReceiverRetired || !Godot.GodotObject.IsInstanceValid(this) ||
            Volatile.Read(ref _released) != 0 || _sourceEffects is null || !Enabled)
            throw new InvalidOperationException("Source image receiver/program factory is not currently alive.");
        if (Volatile.Read(ref _sourceDrawFailure) is { } failure)
            throw new InvalidOperationException("Source image-space draw retains its actual native consumer failure.", failure);
    }

    internal void ResetSourceOutputScalars()
    {
        RequireSourceReceiverFactory();
        _sourceDoubleVision = 0;
        _sourceBlur = 0;
    }

    internal void StoreSourceEffectFields(FalloutSourceImageSpaceOutput value)
    {
        RequireSourceReceiverFactory(); value.Profile.Validate();
        if (value.Radial.Parameters.Length != 4 || value.Radial.Center.Length != 2)
            throw new InvalidDataException("Source radial output changed its actual parameter extent.");
        var writes = new List<SourceImageFieldWrite>();
        void StoreOptional(uint?[] destination, int index, string name, uint? word)
        {
            destination[index] = word; writes.Add(new(name, word));
        }
        _sourceDoubleVision = value.DoubleVision; writes.Add(new("double-vision", value.DoubleVision));
        _sourceBlur = value.Blur; writes.Add(new("blur", value.Blur));
        var radial = _sourceRadial!;
        StoreOptional(radial, 0, "radial.strength", value.Radial.Strength);
        for (var index = 0; index < 4; index++) StoreOptional(radial, index + 1, "radial.parameter." + index, value.Radial.Parameters[index]);
        for (var index = 0; index < 2; index++) StoreOptional(radial, index + 5, "radial.center." + index, value.Radial.Center[index]);
        var options = _sourceOptions!;
        StoreOptional(options, 5, "options.dirty", 1);
        StoreOptional(options, 0, "options.strength", value.Options.Strength);
        StoreOptional(options, 1, "options.first", value.Options.First);
        StoreOptional(options, 2, "options.second", value.Options.Second);
        StoreOptional(options, 3, "options.third", value.Options.Third);
        bool? changed = options[4] is { } previousMode ? previousMode != value.Options.Mode : null;
        if (changed != false)
        {
            options[4] = value.Options.Mode;
            writes.Add(new("options.mode", value.Options.Mode, Executed: changed));
        }
        byte[] enabled = value.Options.Alternate ? [1, 1, 0, 1] : [1, 1, 1, 0];
        for (var index = 0; index < enabled.Length; index++)
        {
            var previous = _sourceOptionBytes![index];
            if (previous == 0 && enabled[index] != 0) ++_sourceOptionCount;
            else if (previous != 0 && enabled[index] == 0) --_sourceOptionCount;
            _sourceOptionBytes[index] = enabled[index];
            writes.Add(new("options.enabled." + index, enabled[index], _sourceOptionCount));
        }
        var depth = value.Depth;
        static uint? Add(uint? left, uint? right, bool subtract)
        {
            if (left is null || right is null) return null;
            var a = BitConverter.UInt32BitsToSingle(left.Value);
            var b = BitConverter.UInt32BitsToSingle(right.Value);
            return BitConverter.SingleToUInt32Bits(subtract ? FalloutSourceImageSpaceFloat.Subtract(a, b) :
                FalloutSourceImageSpaceFloat.Add(a, b));
        }
        var dof = _sourceDepth!;
        _sourceDofDirty = 1; writes.Add(new("depth.dirty", 1));
        StoreOptional(dof, 0, "depth.strength", depth.Strength);
        uint? widest = _sourceDofRange is { } previousRange && depth.FocusDistance is { } distance
            ? BitConverter.UInt32BitsToSingle(distance) > BitConverter.UInt32BitsToSingle(previousRange) ? distance : previousRange : null;
        StoreOptional(dof, 6, "depth.focus", depth.FocusDistance);
        StoreOptional(dof, 4, "depth.transient-lower", Add(depth.FocusDistance, widest, true));
        StoreOptional(dof, 5, "depth.transient-upper", Add(depth.FocusDistance, widest, false));
        _sourceDofRange = depth.FocusRange; writes.Add(new("depth.range", depth.FocusRange));
        StoreOptional(dof, 4, "depth.lower", Add(depth.FocusDistance, depth.FocusRange, true));
        StoreOptional(dof, 5, "depth.upper", Add(depth.FocusRange, depth.FocusDistance, false));
        // Unknown comparison inputs propagate unknown conditional outputs;
        // null == null in C# must never certify a skipped source mode writer.
        bool? dofChanged = depth.Mode is { } incomingMode && _sourceDofMode is { } oldMode ? incomingMode != oldMode : null;
        if (dofChanged != false)
        {
            _sourceDofMode = depth.Mode; writes.Add(new("depth.mode", depth.Mode, Executed: dofChanged));
            uint? first = depth.Mode is { } mode ? BitConverter.SingleToUInt32Bits(mode is 0 or 1 ? 1f : 0f) : null;
            uint? second = depth.Mode is { } secondMode ? BitConverter.SingleToUInt32Bits(secondMode is 0 or 2 ? 1f : 0f) : null;
            dof[1] = first; dof[2] = second;
            writes.Add(new("depth.first-enabled", first, Executed: dofChanged));
            writes.Add(new("depth.second-enabled", second, Executed: dofChanged));
        }
        StoreOptional(dof, 8, "depth.parameter-strength", depth.Strength);
        StoreOptional(dof, 9, "depth.first", depth.First);
        StoreOptional(dof, 10, "depth.second", depth.Second);
        StoreOptional(_sourceScalar!, 1, "scalar.dirty", 1);
        StoreOptional(_sourceScalar!, 0, "scalar.value", value.Scalar);
        _sourceNativeFields = new(value,
            new(options[0]!.Value, options[1]!.Value, options[2]!.Value, options[3]!.Value, options[4]!.Value,
                (byte[])_sourceOptionBytes!.Clone(), 4, _sourceOptionCount, (byte)options[5]!.Value),
            new(dof[0]!.Value, dof[9], dof[10], dof[6], _sourceDofRange, dof[4], dof[5], _sourceDofMode,
                dof[1] is { } near ? BitConverter.UInt32BitsToSingle(near) != 0 : null,
                dof[2] is { } far ? BitConverter.UInt32BitsToSingle(far) != 0 : null, _sourceDofDirty, (uint?[])dof.Clone()),
            new(radial[0]!.Value, radial[1..5].Select(value => value!.Value).ToArray(),
                radial[5..7].Select(value => value!.Value).ToArray(), (byte)radial[7]!.Value), value.Scalar, 1, [], writes.ToArray());
    }

    internal void StoreSourceProfileGlobals(FalloutSourceImageSpaceOutput value)
    {
        RequireSourceReceiverFactory();
        if (_sourceNativeFields is not { } fields || fields.Output.Invocation != value.Invocation ||
            fields.Output.Process != value.Process || fields.Output.Mutation != value.Mutation)
            throw new InvalidOperationException("Source image profile/global child lost its actual preceding effects publication.");
        _sourceNativeFields = fields with
        {
            Globals =
            [value.Profile.ReadWord(8), value.Profile.ReadWord(11), value.Profile.ReadWord(13), value.Profile.ReadWord(12), value.Profile.ReadWord(2)]
        };
    }

    internal void RetireSourceReceiverFields()
    {
        if (!_sourceReceiverConstructionEntered || _sourceReceiverRetired) return;
        for (var index = _sourceAllocations.Count - 1; index >= 0; --index)
            _sourceAllocation.Release(_sourceAllocations[index]);
        _sourceAllocations.Clear(); _sourceAllocation.Retire(); _sourceReceiverRetired = true;
    }
}
