namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class FalloutNifFile
{
    private FalloutNifParticleSystem ReadParticleSystem(FalloutNifBlock block, ref NifCursor cursor) =>
        new(block, ReadGeometry(block, ref cursor), cursor.ReadBoolean("particle world space"),
            ReadReferences(ref cursor, "particle modifiers"));

    private FalloutNifParticleData ReadParticleData(FalloutNifBlock block, ref NifCursor cursor)
    {
        var group = cursor.ReadInt32("particle group");
        var maximum = cursor.ReadUInt16("particle capacity");
        var keep = cursor.ReadByte("particle keep flags");
        var compress = cursor.ReadByte("particle compression flags");
        var vertices = cursor.ReadBoolean("particle vertices");
        var flags = cursor.ReadUInt16("particle data flags");
        var normals = cursor.ReadBoolean("particle normals");
        var center = ReadVector(ref cursor, "particle bound center");
        var radius = cursor.ReadFiniteSingle("particle bound radius");
        var colors = cursor.ReadBoolean("particle colors");
        var consistency = cursor.ReadUInt16("particle consistency");
        var additional = ReadReference(ref cursor, "particle additional data");
        // Bethesda 20.2 keeps the capacity and presence bits, but serializes no
        // per-particle vertex/radius/color arrays. They are runtime state.
        var radii = cursor.ReadBoolean("particle radii");
        var active = cursor.ReadUInt16("particle active count");
        var sizes = cursor.ReadBoolean("particle sizes");
        var rotations = cursor.ReadBoolean("particle rotations");
        var angles = cursor.ReadBoolean("particle rotation angles");
        var axes = cursor.ReadBoolean("particle rotation axes");
        var textureIndices = cursor.ReadBoolean("particle texture indices");
        var offsets = new FalloutNifVector4[cursor.ReadByte("particle subtexture count")];
        for (var i = 0; i < offsets.Length; i++) offsets[i] = ReadVector4(ref cursor, "particle subtexture");
        var speeds = cursor.ReadBoolean("particle rotation speeds");
        return new(block, maximum, group, keep, compress, vertices, flags, normals, center, radius,
            colors, consistency, additional, radii, active, sizes, rotations, angles, axes, textureIndices, offsets, speeds);
    }

    private FalloutNifParticleModifier ReadParticleModifier(FalloutNifBlock block, ref NifCursor cursor)
    {
        var name = ReadStringReference(ref cursor, "particle modifier name");
        var order = cursor.ReadUInt32("particle modifier order");
        var target = ReadReference(ref cursor, "particle modifier target");
        var active = cursor.ReadBoolean("particle modifier active");
        var header = new FalloutNifParticleModifier(block, name, order, target, active);
        switch (block.TypeName)
        {
            case "NiPSysMeshEmitter":
            case "NiPSysBoxEmitter":
            case "NiPSysCylinderEmitter":
            case "NiPSysSphereEmitter":
                var emitter = new FalloutNifParticleEmitter(header,
                    cursor.ReadFiniteSingle("emitter speed"), cursor.ReadFiniteSingle("emitter speed variation"),
                    cursor.ReadFiniteSingle("emitter declination"), cursor.ReadFiniteSingle("emitter declination variation"),
                    cursor.ReadFiniteSingle("emitter planar angle"), cursor.ReadFiniteSingle("emitter planar variation"),
                    ReadVector4(ref cursor, "emitter color"), cursor.ReadFiniteSingle("emitter radius"),
                    cursor.ReadFiniteSingle("emitter radius variation"), cursor.ReadFiniteSingle("emitter life"),
                    cursor.ReadFiniteSingle("emitter life variation"));
                if (block.TypeName == "NiPSysBoxEmitter")
                    return new FalloutNifParticleBoxEmitter(emitter, ReadReference(ref cursor, "emitter object"),
                        ReadVector(ref cursor, "emitter dimensions"));
                if (block.TypeName == "NiPSysCylinderEmitter")
                    return new FalloutNifParticleCylinderEmitter(emitter, ReadReference(ref cursor, "emitter object"),
                        cursor.ReadFiniteSingle("cylinder radius"), cursor.ReadFiniteSingle("cylinder height"));
                if (block.TypeName == "NiPSysSphereEmitter")
                    return new FalloutNifParticleSphereEmitter(emitter, ReadReference(ref cursor, "emitter object"),
                        cursor.ReadFiniteSingle("sphere radius"));
                return new FalloutNifParticleMeshEmitter(emitter, ReadReferences(ref cursor, "emitter meshes"),
                    cursor.ReadUInt32("emitter velocity type"), cursor.ReadUInt32("emission type"),
                    ReadVector(ref cursor, "emission axis"));
            case "NiPSysAgeDeathModifier":
                return new FalloutNifParticleAgeDeath(header, cursor.ReadBoolean("spawn on death"),
                    ReadReference(ref cursor, "death spawn modifier"));
            case "NiPSysSpawnModifier":
                return new FalloutNifParticleSpawn(header, cursor.ReadUInt16("spawn generations"),
                    cursor.ReadFiniteSingle("spawn probability"), cursor.ReadUInt16("spawn minimum"),
                    cursor.ReadUInt16("spawn maximum"), cursor.ReadFiniteSingle("spawn speed variation"),
                    cursor.ReadFiniteSingle("spawn direction variation"), cursor.ReadFiniteSingle("spawn life"),
                    cursor.ReadFiniteSingle("spawn life variation"));
            case "NiPSysGrowFadeModifier":
                return new FalloutNifParticleGrowFade(header, cursor.ReadFiniteSingle("grow time"),
                    cursor.ReadUInt16("grow generation"), cursor.ReadFiniteSingle("fade time"),
                    cursor.ReadUInt16("fade generation"), cursor.ReadFiniteSingle("particle base scale"));
            case "BSPSysSimpleColorModifier":
                return new FalloutNifParticleColor(header, cursor.ReadFiniteSingle("color fade in"),
                    cursor.ReadFiniteSingle("color fade out"), cursor.ReadFiniteSingle("color 1 end"),
                    cursor.ReadFiniteSingle("color 1 start"), cursor.ReadFiniteSingle("color 2 end"),
                    cursor.ReadFiniteSingle("color 2 start"), ReadVector4(ref cursor, "color 1"),
                    ReadVector4(ref cursor, "color 2"), ReadVector4(ref cursor, "color 3"));
            case "NiPSysRotationModifier":
                return new FalloutNifParticleRotation(header, cursor.ReadFiniteSingle("rotation speed"),
                    cursor.ReadFiniteSingle("rotation speed variation"), cursor.ReadFiniteSingle("rotation angle"),
                    cursor.ReadFiniteSingle("rotation angle variation"), cursor.ReadBoolean("random rotation sign"),
                    cursor.ReadBoolean("random rotation axis"), ReadVector(ref cursor, "rotation axis"));
            case "NiPSysBombModifier":
                return new FalloutNifParticleBomb(header, ReadReference(ref cursor, "bomb object"),
                    ReadVector(ref cursor, "bomb axis"), cursor.ReadFiniteSingle("bomb decay"),
                    cursor.ReadFiniteSingle("bomb delta velocity"), cursor.ReadUInt32("bomb decay type"),
                    cursor.ReadUInt32("bomb symmetry"));
            case "NiPSysGravityModifier":
                return new FalloutNifParticleGravity(header, ReadReference(ref cursor, "gravity object"),
                    ReadVector(ref cursor, "gravity axis"), cursor.ReadFiniteSingle("gravity decay"),
                    cursor.ReadFiniteSingle("gravity strength"), cursor.ReadUInt32("gravity force type"),
                    cursor.ReadFiniteSingle("gravity turbulence"), cursor.ReadFiniteSingle("gravity turbulence scale"),
                    cursor.ReadBoolean("gravity world aligned"));
            case "NiPSysDragModifier":
                return new FalloutNifParticleDrag(header, ReadReference(ref cursor, "drag object"), ReadVector(ref cursor, "drag axis"),
                    cursor.ReadFiniteSingle("drag percentage"), cursor.ReadFiniteSingle("drag range"), cursor.ReadFiniteSingle("drag falloff"));
            case "NiPSysBoundUpdateModifier":
                return new FalloutNifParticleBounds(header, cursor.ReadUInt16("bound update skip"));
            case "NiPSysPositionModifier":
                return header;
            default: throw new NotSupportedException($"Particle modifier {block.TypeName} is unbound.");
        }
    }

    private FalloutNifParticleController ReadParticleController(FalloutNifBlock block, ref NifCursor cursor)
    {
        var time = ReadTimeController(ref cursor, "particle controller");
        if (block.TypeName == "NiPSysUpdateCtlr") return new(block, time, -1, string.Empty, -1);
        var interpolator = ReadReference(ref cursor, "particle interpolator");
        var name = ReadStringReference(ref cursor, "controlled particle modifier");
        var active = block.TypeName is "NiPSysEmitterCtlr" or "BSPSysMultiTargetEmitterCtlr"
            ? ReadReference(ref cursor, "emitter active interpolator") : -1;
        return new(block, time, interpolator, name, active)
        {
            MaximumEmitters = block.TypeName == "BSPSysMultiTargetEmitterCtlr" ? cursor.ReadUInt16("emitter capacity") : null,
            Master = block.TypeName == "BSPSysMultiTargetEmitterCtlr" ? ReadReference(ref cursor, "emitter master") : -1
        };
    }

    private static FalloutNifBlendBoolInterpolator ReadBlendBoolInterpolator(FalloutNifBlock block, ref NifCursor cursor)
    {
        var blend = ReadManagerBlendInterpolator(ref cursor, "blend bool interpolator");
        return new(block, blend.Flags, blend.ArraySize, blend.WeightThreshold, cursor.ReadByte("blend bool value"));
    }

    private FalloutNifPathInterpolator ReadPathInterpolator(FalloutNifBlock block, ref NifCursor cursor) =>
        new(block, cursor.ReadUInt16("path flags"), cursor.ReadInt32("path bank direction"),
            cursor.ReadFiniteSingle("path bank angle"), cursor.ReadFiniteSingle("path smoothing"),
            unchecked((short)cursor.ReadUInt16("path follow axis")), ReadReference(ref cursor, "path position data"),
            ReadReference(ref cursor, "path percent data"));
}

internal sealed record FalloutNifParticleSystem(FalloutNifBlock Block, FalloutNifGeometry Geometry,
    bool WorldSpace, int[] Modifiers) : FalloutNifObject(Block);
internal sealed record FalloutNifParticleData(FalloutNifBlock Block, ushort Maximum, int Group, byte Keep, byte Compress,
    bool HasVertices, ushort Flags, bool HasNormals, FalloutNifVector3 Center, float Radius, bool HasColors,
    ushort Consistency, int Additional, bool HasRadii, ushort Active, bool HasSizes, bool HasRotations,
    bool HasAngles, bool HasAxes, bool HasTextureIndices, FalloutNifVector4[] Subtextures, bool HasRotationSpeeds) : FalloutNifObject(Block);
internal record FalloutNifParticleModifier(FalloutNifBlock Block, string Name, uint Order, int Target, bool Active) : FalloutNifObject(Block);
internal record FalloutNifParticleEmitter(FalloutNifParticleModifier Header, float Speed, float SpeedVariation,
    float Declination, float DeclinationVariation, float PlanarAngle, float PlanarVariation, FalloutNifVector4 Color,
    float Radius, float RadiusVariation, float Life, float LifeVariation)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal abstract record FalloutNifParticleVolumeEmitter(FalloutNifParticleEmitter Emitter, int Object)
    : FalloutNifParticleModifier(Emitter.Block, Emitter.Name, Emitter.Order, Emitter.Target, Emitter.Active);
internal sealed record FalloutNifParticleBoxEmitter(FalloutNifParticleEmitter Emitter, int Object, FalloutNifVector3 Dimensions)
    : FalloutNifParticleVolumeEmitter(Emitter, Object);
internal sealed record FalloutNifParticleCylinderEmitter(FalloutNifParticleEmitter Emitter, int Object, float CylinderRadius, float Height)
    : FalloutNifParticleVolumeEmitter(Emitter, Object);
internal sealed record FalloutNifParticleSphereEmitter(FalloutNifParticleEmitter Emitter, int Object, float SphereRadius)
    : FalloutNifParticleVolumeEmitter(Emitter, Object);
internal sealed record FalloutNifParticleMeshEmitter(FalloutNifParticleEmitter Emitter, int[] Meshes, uint VelocityType,
    uint EmissionType, FalloutNifVector3 Axis)
    : FalloutNifParticleModifier(Emitter.Block, Emitter.Name, Emitter.Order, Emitter.Target, Emitter.Active);
internal sealed record FalloutNifParticleAgeDeath(FalloutNifParticleModifier Header, bool SpawnOnDeath, int Spawn)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleSpawn(FalloutNifParticleModifier Header, ushort Generations, float Probability,
    ushort Minimum, ushort Maximum, float SpeedVariation, float DirectionVariation, float Life, float LifeVariation)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleGrowFade(FalloutNifParticleModifier Header, float Grow, ushort GrowGeneration,
    float Fade, ushort FadeGeneration, float Scale)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleColor(FalloutNifParticleModifier Header, float FadeIn, float FadeOut,
    float FirstEnd, float FirstStart, float SecondEnd, float SecondStart,
    FalloutNifVector4 First, FalloutNifVector4 Second, FalloutNifVector4 Third)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleRotation(FalloutNifParticleModifier Header, float Speed, float SpeedVariation,
    float Angle, float AngleVariation, bool RandomSign, bool RandomAxis, FalloutNifVector3 Axis)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleBomb(FalloutNifParticleModifier Header, int Object, FalloutNifVector3 Axis,
    float Decay, float DeltaVelocity, uint DecayType, uint Symmetry)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleGravity(FalloutNifParticleModifier Header, int Object, FalloutNifVector3 Axis,
    float Decay, float Strength, uint ForceType, float Turbulence, float TurbulenceScale, bool WorldAligned)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleDrag(FalloutNifParticleModifier Header, int Object, FalloutNifVector3 Axis,
    float Percentage, float Range, float Falloff)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleBounds(FalloutNifParticleModifier Header, ushort Skip)
    : FalloutNifParticleModifier(Header.Block, Header.Name, Header.Order, Header.Target, Header.Active);
internal sealed record FalloutNifParticleController(FalloutNifBlock Block, FalloutNifTimeController Time,
    int Interpolator, string Modifier, int ActiveInterpolator) : FalloutNifObject(Block)
{
    public ushort? MaximumEmitters { get; init; }
    public int Master { get; init; } = -1;
}
internal sealed record FalloutNifBlendBoolInterpolator(FalloutNifBlock Block, byte Flags, byte ArraySize,
    float WeightThreshold, byte Value) : FalloutNifObject(Block);
internal sealed record FalloutNifPathInterpolator(FalloutNifBlock Block, ushort Flags, int BankDirection,
    float BankAngle, float Smoothing, short FollowAxis, int PathData, int PercentData) : FalloutNifObject(Block);
internal sealed record FalloutNifAlphaController(FalloutNifBlock Block, FalloutNifTimeController Time,
    int Interpolator) : FalloutNifObject(Block);
