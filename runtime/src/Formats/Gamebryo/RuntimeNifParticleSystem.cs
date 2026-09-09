using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Source declarations and clocks are instance-owned. The source NIF supplies
// emission geometry, capacity, texture, curves and forces; the renderer supplies
// camera-facing particle quads, never an authored replacement effect.
internal sealed partial class RuntimeNifParticleSystem : Node3D
{
    private FalloutNifParticleSystem _source = null!;
    private FalloutNifParticleData _data = null!;
    private FalloutNifParticleModifier[] _modifiers = [];
    private readonly Dictionary<string, bool> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _rates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _remainders = new(StringComparer.Ordinal);
    private readonly Dictionary<int, FalloutNifMeshData> _meshes = [];
    private IReadOnlyDictionary<int, Node3D> _nodes = null!;
    private readonly Random _random = new();
    private Particle[] _particles = [];
    private float[] _drawBuffer = [];
    private MultiMesh _draw = null!;
    private MultiMeshInstance3D _visual = null!;
    private Aabb _publishedBounds;
    private double _nextBoundsRefresh;
    private int _visibleCount;
    private readonly ParticleDistanceOrder _distanceOrder = new();
    private float _units;
    internal long BoundsPublications { get; private set; }
    internal int ActiveCount { get; private set; }
    internal long BirthCount { get; private set; }
    internal long DeathCount { get; private set; }
    internal double SimulatedSeconds { get; private set; }
    internal bool EmissionEnabled { get; set; } = true;
    internal void SetOutputEncoding(bool encoded) => ((ShaderMaterial)_draw.Mesh.SurfaceGetMaterial(0)).SetShaderParameter("source_store_encoded", encoded);
    internal void ResetCompleted()
    {
        if (ActiveCount != 0) throw new InvalidOperationException("Live particles cannot be recycled.");
        foreach (var modifier in _modifiers) _active[modifier.Name] = modifier.Active;
        foreach (var name in _rates.Keys) { _rates[name] = 0; _remainders[name] = 0; }
        BirthCount = DeathCount = 0; SimulatedSeconds = 0; EmissionEnabled = false;
        _nextBoundsRefresh = 0; _publishedBounds = default;
    }
    internal IReadOnlyList<Vector3> Positions => _particles.Take(ActiveCount).Select(value => value.Position).ToArray();
    internal object Observation => new
    {
        block = _source.Block.Index,
        worldSpace = _source.WorldSpace,
        capacity = _particles.Length,
        active = ActiveCount,
        births = BirthCount,
        deaths = DeathCount,
        seconds = SimulatedSeconds,
        modifiers = _modifiers.Select(modifier => new
        {
            block = modifier.Block.Index,
            modifier.Name,
            modifier.Order,
            active = _active[modifier.Name],
            rate = _rates.GetValueOrDefault(modifier.Name),
            remainder = _remainders.GetValueOrDefault(modifier.Name)
        }).ToArray(),
        // Packed fields preserve float32 values, including signed zero; the
        // render trace stores these exact bytes alongside the MultiMesh buffer.
        encoding = "particle:position3,velocity3,age,life,radius,angle,spin,color4:f32-le;texture:i32-le",
        state = System.Convert.ToBase64String(ObservationBytes()),
        missing = new[] { "retail-particle-identity-and-random-sequence-join", "retail-modifier-motion-parity" },
    };

    private byte[] ObservationBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var particle in _particles.AsSpan(0, ActiveCount))
        {
            writer.Write(particle.Position.X); writer.Write(particle.Position.Y); writer.Write(particle.Position.Z);
            writer.Write(particle.Velocity.X); writer.Write(particle.Velocity.Y); writer.Write(particle.Velocity.Z);
            writer.Write(particle.Age); writer.Write(particle.Life); writer.Write(particle.Radius);
            writer.Write(particle.Angle); writer.Write(particle.Spin);
            writer.Write(particle.InitialColor.R); writer.Write(particle.InitialColor.G);
            writer.Write(particle.InitialColor.B); writer.Write(particle.InitialColor.A); writer.Write(particle.Texture);
        }
        return stream.ToArray();
    }

    private struct Particle
    {
        internal Vector3 Position, Velocity;
        internal float Age, Life, Radius, Angle, Spin;
        internal int Texture;
        internal Color InitialColor;
    }

    private sealed class ParticleDistanceOrder : IComparer<Particle>
    {
        internal Vector3 Origin;
        public int Compare(Particle a, Particle b) =>
            b.Position.DistanceSquaredTo(Origin).CompareTo(a.Position.DistanceSquaredTo(Origin));
    }

    internal void Configure(FalloutNifFile file, FalloutNifParticleSystem source,
        IReadOnlyDictionary<int, Node3D> nodes, Material material, float units)
    {
        _source = source; _nodes = nodes; _units = units;
        _data = file.ReadObject(source.Geometry.Data) as FalloutNifParticleData ??
            throw new InvalidDataException("Particle system has no particle data.");
        if (_data.Maximum == 0 || _data.Additional != -1 || _data.Active != 0 || _data.HasNormals || _data.HasRotations ||
            _data.HasAxes || _data.Keep != 0 || _data.Compress != 0 || _data.Flags != 0 || _data.Consistency != 0 ||
            _data.HasTextureIndices != (_data.Subtextures.Length != 0))
            throw new NotSupportedException($"Particle data {_data.Block.Index} has unsupported runtime fields.");
        _modifiers = source.Modifiers.Select(index => file.ReadObject(index) as FalloutNifParticleModifier ??
            throw new InvalidDataException("Particle modifier reference is not a modifier.")).OrderBy(value => value.Order).ToArray();
        foreach (var modifier in _modifiers)
        {
            if (modifier.Target != source.Block.Index || !_active.TryAdd(modifier.Name, modifier.Active))
                throw new InvalidDataException("Particle modifier has a wrong target or duplicate name.");
            switch (modifier)
            {
                case FalloutNifParticleVolumeEmitter volume:
                    RequireNode(volume.Object); ValidateEmitter(volume.Emitter);
                    if (volume switch
                    {
                        FalloutNifParticleBoxEmitter box => box.Dimensions.X < 0 || box.Dimensions.Y < 0 || box.Dimensions.Z < 0,
                        FalloutNifParticleCylinderEmitter cylinder => cylinder.CylinderRadius < 0 || cylinder.Height < 0,
                        FalloutNifParticleSphereEmitter sphere => sphere.SphereRadius < 0,
                        _ => true,
                    }) throw new InvalidDataException("Particle volume has invalid dimensions.");
                    break;
                case FalloutNifParticleMeshEmitter mesh:
                    ValidateEmitter(mesh.Emitter);
                    if (mesh.Meshes.Length == 0 || mesh.VelocityType > 2 || mesh.EmissionType is not (0 or 3))
                        throw new NotSupportedException("Particle mesh emission mode has no sampler.");
                    foreach (var index in mesh.Meshes)
                    {
                        RequireNode(index);
                        var data = file.ReadMeshData(file.ReadGeometry(index).Data);
                        if (data.Vertices.Length == 0 || mesh.VelocityType == 0 && data.Normals.Length != data.Vertices.Length ||
                            mesh.EmissionType == 3 && data.Triangles.Length == 0)
                            throw new InvalidDataException("Particle emitter mesh is incomplete.");
                        _meshes.TryAdd(index, data);
                    }
                    break;
                case FalloutNifParticleAgeDeath { SpawnOnDeath: true }:
                case FalloutNifParticleSpawn { Generations: > 0 }:
                    throw new NotSupportedException("Secondary particle generations have no runtime owner.");
                case FalloutNifParticleGrowFade grow when grow.Grow < 0 || grow.Fade < 0 || grow.Scale < 0:
                    throw new InvalidDataException("Particle growth contains a negative duration or scale.");
                case FalloutNifParticleGravity gravity:
                    RequireNode(gravity.Object);
                    if (gravity.ForceType != 0 || gravity.Decay != 0 || gravity.Turbulence != 0)
                        throw new NotSupportedException("Particle gravity requires a declared directional force without turbulence.");
                    break;
                case FalloutNifParticleDrag drag:
                    RequireNode(drag.Object);
                    if (drag.Percentage < 0 || drag.Range < 0 || drag.Falloff < 0 || Convert(drag.Axis).LengthSquared() == 0)
                        throw new InvalidDataException("Particle drag has invalid strength, extent or axis.");
                    break;
                case FalloutNifParticleBomb bomb:
                    RequireNode(bomb.Object);
                    if (bomb.DecayType > 2 || bomb.Symmetry > 2 || bomb.Decay < 0 || Convert(bomb.Axis).LengthSquared() == 0)
                        throw new NotSupportedException("Particle bomb force is outside the declared symmetry/decay contract.");
                    break;
            }
        }
        if (!_modifiers.OfType<FalloutNifParticleAgeDeath>().Any() ||
            !_modifiers.Any(value => value.Block.TypeName == "NiPSysPositionModifier") ||
            !_modifiers.OfType<FalloutNifParticleBounds>().Any())
            throw new NotSupportedException("Particle system lacks lifetime, position or bounds ownership.");
        _particles = new Particle[_data.Maximum];
        _drawBuffer = new float[checked(_data.Maximum * NativeParticleDrawBuffer.Stride)];
        if (material is not ShaderMaterial shader || shader.ResourceName != NativeNifEffectMaterial.ResourceIdentity)
            throw new NotSupportedException("Particle material has no admitted source shader.");
        shader.SetShaderParameter("source_particle_atlas", _data.HasTextureIndices);
        _draw = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One * 2, Material = shader },
            InstanceCount = _data.Maximum,
            VisibleInstanceCount = 0,
        };
        _visual = new MultiMeshInstance3D
        {
            Name = "SourceParticles",
            Multimesh = _draw,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_visual);
        SetMeta("opennv_particle_source_capacity", _data.Maximum);
        SetMeta("opennv_particle_owner", "direct-nif-instance-simulation");
    }

    private void ValidateEmitter(FalloutNifParticleEmitter emitter)
    {
        if (emitter.Radius < 0 || emitter.RadiusVariation < 0 || emitter.Life <= 0 || emitter.LifeVariation < 0 ||
            emitter.SpeedVariation < 0 || emitter.DeclinationVariation < 0 || emitter.PlanarVariation < 0)
            throw new InvalidDataException("Particle emitter has an invalid range.");
        _rates.Add(emitter.Name, 0); _remainders.Add(emitter.Name, 0);
    }

    private void RequireNode(int index)
    {
        if (!_nodes.ContainsKey(index)) throw new InvalidDataException($"Particle transform {index} is not reachable.");
    }

    internal RuntimeNifControllerChannel Bind(FalloutNifFile file, FalloutNifControllerLink link)
    {
        if (!_active.ContainsKey(link.Variable1)) throw new InvalidDataException("Particle controller targets a missing modifier.");
        if (link.ControllerType == "NiPSysEmitterCtlr" && link.Variable2 == "BirthRate" && _rates.ContainsKey(link.Variable1))
        {
            var sampler = new FalloutNifFloatAnimation(file, link.Interpolator);
            return new(time => _rates[link.Variable1] = Math.Max(0, sampler.Sample(time)));
        }
        if (link.ControllerType == "NiPSysEmitterCtlr" && link.Variable2 == "EmitterActive" && _rates.ContainsKey(link.Variable1) ||
            link.ControllerType == "NiPSysModifierActiveCtlr" && link.Variable2.Length == 0)
        {
            var sampler = new FalloutNifBoolAnimation(file, link.Interpolator);
            return new(time => _active[link.Variable1] = sampler.Sample(time));
        }
        throw new NotSupportedException($"Particle channel {link.Variable1}/{link.Variable2} is unsupported.");
    }

    public override void _Ready()
    {
        if (_draw is null) throw new InvalidOperationException("Particle instance has no source configuration.");
        if (_source.WorldSpace) { _visual.TopLevel = true; _visual.GlobalTransform = Transform3D.Identity; }
    }

    public override void _Process(double delta)
    {
        Advance((float)delta);
        Publish();
    }

    internal void Advance(float delta)
    {
        if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        var remaining = delta;
        while (remaining > 0)
        {
            var step = Math.Min(remaining, 1f / 60f);
            Step(step); remaining -= step;
        }
        SimulatedSeconds += delta;
    }

    private void Step(float delta)
    {
        for (var i = ActiveCount - 1; i >= 0; i--)
        {
            _particles[i].Age += delta;
            if (_particles[i].Age >= _particles[i].Life)
            {
                _particles[i] = _particles[--ActiveCount]; DeathCount++;
            }
        }
        foreach (var modifier in _modifiers)
        {
            if (!_active[modifier.Name]) continue;
            if (modifier is FalloutNifParticleVolumeEmitter or FalloutNifParticleMeshEmitter)
            {
                if (!EmissionEnabled) continue;
                var births = _remainders[modifier.Name] + _rates[modifier.Name] * delta;
                var count = (int)births;
                _remainders[modifier.Name] = births - count;
                var available = Math.Min(count, _particles.Length - ActiveCount);
                for (var i = 0; i < available; i++) Emit(modifier);
            }
            else if (modifier is FalloutNifParticleGravity gravity)
            {
                var axis = Convert(gravity.Axis);
                if (!gravity.WorldAligned) axis = TransformOf(gravity.Object).Basis * axis;
                var acceleration = axis.Normalized() * gravity.Strength * _units;
                for (var i = 0; i < ActiveCount; i++) _particles[i].Velocity += acceleration * delta;
            }
            else if (modifier is FalloutNifParticleBomb bomb)
            {
                var transform = TransformOf(bomb.Object);
                var axis = (transform.Basis * Convert(bomb.Axis)).Normalized();
                for (var i = 0; i < ActiveCount; i++)
                {
                    var offset = _particles[i].Position - transform.Origin;
                    var direction = bomb.Symmetry switch
                    {
                        1 => offset - axis * offset.Dot(axis),
                        2 => axis * offset.Dot(axis),
                        _ => offset,
                    };
                    var distance = direction.Length() / _units;
                    var decay = bomb.DecayType switch
                    {
                        1 => bomb.Decay == 0 ? 0 : Math.Max(0, 1 - distance / bomb.Decay),
                        2 => bomb.Decay == 0 ? 0 : MathF.Exp(-distance / bomb.Decay),
                        _ => 1,
                    };
                    _particles[i].Velocity += direction.Normalized() * (bomb.DeltaVelocity * decay * _units * delta);
                }
            }
            else if (modifier is FalloutNifParticleDrag drag)
            {
                var transform = TransformOf(drag.Object);
                var axis = (transform.Basis * Convert(drag.Axis)).Normalized();
                for (var i = 0; i < ActiveCount; i++)
                {
                    var distance = _particles[i].Position.DistanceTo(transform.Origin) / _units;
                    var weight = distance <= drag.Range ? 1 : drag.Falloff == 0 ? 0 : Math.Clamp(1 - (distance - drag.Range) / drag.Falloff, 0, 1);
                    var reduction = Math.Clamp(drag.Percentage * weight * delta, 0, 1);
                    _particles[i].Velocity -= axis * (_particles[i].Velocity.Dot(axis) * reduction);
                }
            }
            else if (modifier.Block.TypeName == "NiPSysPositionModifier")
            {
                for (var i = 0; i < ActiveCount; i++)
                {
                    _particles[i].Position += _particles[i].Velocity * delta;
                    _particles[i].Angle += _particles[i].Spin * delta;
                }
            }
        }
    }

    private void Emit(FalloutNifParticleModifier modifier)
    {
        FalloutNifParticleEmitter emitter;
        Transform3D transform;
        Vector3 point, direction;
        if (modifier is FalloutNifParticleVolumeEmitter volume)
        {
            emitter = volume.Emitter; transform = TransformOf(volume.Object);
            point = VolumePoint(volume) * _units;
            direction = Convert(Direction(emitter));
        }
        else if (modifier is FalloutNifParticleMeshEmitter mesh)
        {
            emitter = mesh.Emitter;
            var index = mesh.Meshes[_random.Next(mesh.Meshes.Length)];
            transform = TransformOf(index);
            var geometry = _meshes[index];
            var vertex = _random.Next(geometry.Vertices.Length);
            point = Convert(geometry.Vertices[vertex]);
            direction = mesh.VelocityType == 0 ? Convert(geometry.Normals[vertex]) :
                mesh.VelocityType == 2 ? Convert(mesh.Axis) : RandomDirection();
            if (mesh.EmissionType == 3)
            {
                var triangle = geometry.Triangles[_random.Next(geometry.Triangles.Length)];
                var a = (float)_random.NextDouble(); var b = (float)_random.NextDouble();
                if (a + b > 1) { a = 1 - a; b = 1 - b; }
                point = Convert(geometry.Vertices[triangle.A]) * a + Convert(geometry.Vertices[triangle.B]) * b +
                    Convert(geometry.Vertices[triangle.C]) * (1 - a - b);
                if (mesh.VelocityType == 0)
                    direction = Convert(geometry.Normals[triangle.A]) * a + Convert(geometry.Normals[triangle.B]) * b +
                        Convert(geometry.Normals[triangle.C]) * (1 - a - b);
            }
            point *= _units;
        }
        else throw new InvalidOperationException("Particle source is not an emitter.");
        var particle = new Particle
        {
            Position = transform * point,
            Velocity = (transform.Basis * direction).Normalized() * (Vary(emitter.Speed, emitter.SpeedVariation) * _units),
            Life = Math.Max(float.Epsilon, Vary(emitter.Life, emitter.LifeVariation)),
            Radius = Math.Max(0, Vary(emitter.Radius, emitter.RadiusVariation)) * _units * transform.Basis.Scale.Abs().X,
            InitialColor = ToColor(emitter.Color),
            Texture = _data.Subtextures.Length == 0 ? 0 : _random.Next(_data.Subtextures.Length),
        };
        foreach (var rotation in _modifiers.OfType<FalloutNifParticleRotation>().Where(value => _active[value.Name]))
        {
            particle.Angle = rotation.Angle + rotation.AngleVariation * Centered() * 2;
            particle.Spin = rotation.Speed + rotation.SpeedVariation * Centered() * 2;
            if (rotation.RandomSign && _random.Next(2) == 0) particle.Spin = -particle.Spin;
        }
        _particles[ActiveCount++] = particle; BirthCount++;
    }

    private FalloutNifVector3 Direction(FalloutNifParticleEmitter emitter)
    {
        var declination = Vary(emitter.Declination, emitter.DeclinationVariation);
        var angle = Vary(emitter.PlanarAngle, emitter.PlanarVariation);
        return new(MathF.Sin(declination) * MathF.Cos(angle), MathF.Sin(declination) * MathF.Sin(angle), MathF.Cos(declination));
    }

    private Vector3 VolumePoint(FalloutNifParticleVolumeEmitter volume)
    {
        if (volume is FalloutNifParticleBoxEmitter box)
            return Convert(new(box.Dimensions.X * Centered(), box.Dimensions.Y * Centered(), box.Dimensions.Z * Centered()));
        if (volume is FalloutNifParticleSphereEmitter sphere)
            return RandomDirection() * (MathF.Cbrt(_random.NextSingle()) * sphere.SphereRadius);
        if (volume is FalloutNifParticleCylinderEmitter cylinder)
        {
            var angle = _random.NextSingle() * MathF.Tau; var radius = MathF.Sqrt(_random.NextSingle()) * cylinder.CylinderRadius;
            return Convert(new(radius * MathF.Cos(angle), radius * MathF.Sin(angle), Centered() * cylinder.Height));
        }
        throw new NotSupportedException("Particle volume has no source sampler.");
    }

    private Transform3D TransformOf(int index) => _source.WorldSpace ? _nodes[index].GlobalTransform :
        GlobalTransform.AffineInverse() * _nodes[index].GlobalTransform;
    private float Centered() => (float)_random.NextDouble() - .5f;
    private float Vary(float value, float variation) => value + variation * Centered();
    private Vector3 RandomDirection()
    {
        var z = Centered() * 2; var angle = (float)_random.NextDouble() * MathF.Tau;
        var radius = MathF.Sqrt(1 - z * z);
        return new(radius * MathF.Cos(angle), z, radius * MathF.Sin(angle));
    }

    internal void Publish()
    {
        if (_visibleCount != ActiveCount) { _draw.VisibleInstanceCount = ActiveCount; _visibleCount = ActiveCount; }
        if (ActiveCount == 0) return;
        var camera = GetViewport().GetCamera3D();
        var basis = camera?.GlobalBasis.Orthonormalized() ?? Basis.Identity;
        if (!_source.WorldSpace) basis = GlobalBasis.Inverse() * basis;
        // Alpha quads are sorted within this system; authored material depth
        // and blend state still determine their relationship to the world.
        if (camera is not null)
        {
            _distanceOrder.Origin = _source.WorldSpace ? camera.GlobalPosition : ToLocal(camera.GlobalPosition);
            Array.Sort(_particles, 0, ActiveCount, _distanceOrder);
        }
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = -minimum;
        for (var i = 0; i < ActiveCount; i++)
        {
            ref var particle = ref _particles[i];
            var radius = particle.Radius;
            var color = particle.InitialColor;
            foreach (var modifier in _modifiers)
            {
                if (!_active[modifier.Name]) continue;
                if (modifier is FalloutNifParticleGrowFade grow)
                {
                    radius *= GrowFadeScale(grow, particle.Age, particle.Life);
                }
                else if (modifier is FalloutNifParticleColor colors) color = ParticleColor(colors, particle.Age / particle.Life);
            }
            var transform = new Transform3D(basis * new Basis(Vector3.Back, particle.Angle) *
                Basis.FromScale(Vector3.One * radius), particle.Position);
            var extent = transform.Basis.X.Abs() + transform.Basis.Y.Abs();
            minimum = minimum.Min(particle.Position - extent); maximum = maximum.Max(particle.Position + extent);
            var uv = _data.Subtextures.Length == 0 ? new FalloutNifVector4(0, 1, 0, 1) : _data.Subtextures[particle.Texture];
            NativeParticleDrawBuffer.Write(_drawBuffer.AsSpan(i * NativeParticleDrawBuffer.Stride), transform, color, ToColor(uv));
        }
        var bounds = new Aabb(minimum, maximum - minimum);
        if (!_publishedBounds.Encloses(bounds) || SimulatedSeconds >= _nextBoundsRefresh)
        {
            // Conservative slack avoids a renderer bounds update for every
            // moving quad. Escaping particles expand it immediately; periodic
            // refits keep dead particles from leaving an ever-growing box.
            _publishedBounds = bounds.Grow(Math.Max(_units, bounds.Size.Length() * .125f));
            _draw.CustomAabb = _publishedBounds;
            _nextBoundsRefresh = SimulatedSeconds + 1;
            BoundsPublications++;
        }
        _draw.Buffer = _drawBuffer;
    }

    internal static Color ParticleColor(FalloutNifParticleColor source, float fraction)
    {
        var first = ToColor(source.First); var second = ToColor(source.Second); var third = ToColor(source.Third);
        var color = fraction < source.FirstEnd ? first : fraction < source.FirstStart ?
            first.Lerp(second, (fraction - source.FirstEnd) / (source.FirstStart - source.FirstEnd)) :
            fraction <= source.SecondEnd || source.SecondStart == 0 ? second : fraction < source.SecondStart ?
            second.Lerp(third, (fraction - source.SecondEnd) / (source.SecondStart - source.SecondEnd)) : third;
        color.A = fraction < source.FadeIn ? Mathf.Lerp(first.A, second.A, fraction / source.FadeIn) :
            fraction > source.FadeOut ? Mathf.Lerp(second.A, third.A, (fraction - source.FadeOut) / (1 - source.FadeOut)) : second.A;
        return color;
    }

    internal static float GrowFadeScale(FalloutNifParticleGrowFade source, float age, float life, ushort generation = 0)
    {
        // Base Scale is the endpoint of the growth/shrink envelope, not a
        // multiplier on the entire particle. Overlapping envelopes use their
        // minimum. A zero endpoint therefore still permits full-sized blood,
        // smoke and other particles during their authored lifetime.
        var grow = source.GrowGeneration == generation && source.Grow > 0 ? Math.Min(1, age / source.Grow) : 1;
        var fade = source.FadeGeneration == generation && source.Fade > 0 ? Math.Min(1, (life - age) / source.Fade) : 1;
        var range = 1 - source.Scale;
        return Math.Max(.0001f, (float)((double)Math.Min(grow, fade) * range + source.Scale));
    }

    private static Vector3 Convert(FalloutNifVector3 value) => GamebryoCoordinate.ConvertVector(new Vector3(value.X, value.Y, value.Z));
    private static Color ToColor(FalloutNifVector4 value) => new(value.X, value.Y, value.Z, value.W);
}
