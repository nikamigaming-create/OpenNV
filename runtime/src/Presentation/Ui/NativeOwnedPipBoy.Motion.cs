using Godot;
using OpenNV.Runtime.Content;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoy
{
    private sealed class RadiationNeedleMotion
    {
        private const float MaximumRads = 1000;
        private const float RadiansPerMillisecond = .0005f;
        private sealed record Source(float Interval, float Variance);
        private static readonly ConditionalWeakTable<RuntimeLiveContentSource, Source> Sources = new();
        private readonly Node3D _node;
        private readonly Transform3D _rest;
        private readonly Source _source;
        private readonly Random _random = new();
        private double _seconds, _nextUpdate;
        private float _position, _target;
        internal object State => new
        {
            positionRadians = _position,
            targetRadians = _target,
            updateIntervalSeconds = _source.Interval,
            varianceFraction = _source.Variance,
            source = "owned-RadNeedle;StatsMenu-user3-user4;authoritative-player-radiation",
            randomStreamCorrespondence = "unverified-independent-presentation-stream"
        };

        internal RadiationNeedleMotion(NativeOwnedDeviceSurface surface, float rads)
        {
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned device content is absent.");
            _source = Sources.GetValue(content, _ =>
            {
                var root = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/main/stats_menu.xml")).Elements("menu").Single();
                var interval = float.Parse(root.Element("user3")?.Value ?? throw new InvalidDataException("StatsMenu has no radiation update interval."), CultureInfo.InvariantCulture);
                var variance = float.Parse(root.Element("user4")?.Value ?? throw new InvalidDataException("StatsMenu has no radiation variance."), CultureInfo.InvariantCulture);
                if (!float.IsFinite(interval) || interval <= 0 || !float.IsFinite(variance) || variance < 0)
                    throw new InvalidDataException("StatsMenu radiation timing or variance is invalid.");
                return new(interval, variance);
            });
            _node = surface.Root.FindChildren("*", "", true, false).OfType<Node3D>()
                .Single(node => node.GetMeta("opennv_nif_source_name", "").AsString() == "RadNeedle");
            _rest = _node.Transform;
            _position = _target = Angle(rads, 0);
            Publish();
        }

        internal void Advance(double delta, float rads)
        {
            if (!double.IsFinite(delta) || delta < 0) throw new InvalidDataException("Pip-Boy radiation needle clock is invalid.");
            _seconds += delta;
            if (_seconds >= _nextUpdate)
            {
                // A delayed publication schedules one new target from now; it
                // does not replay missed random draws in a catch-up loop.
                _target = Angle(rads, ((float)_random.NextDouble() - .5f) * _source.Variance * MaximumRads);
                _nextUpdate = _seconds + _source.Interval;
            }
            _position = Mathf.MoveToward(_position, _target, (float)(delta * 1000 * RadiansPerMillisecond));
            Publish();
        }

        private static float Angle(float rads, float noise)
        {
            if (!float.IsFinite(rads)) throw new InvalidDataException("Player radiation is not finite.");
            return MathF.PI / 2 - MathF.PI * (MathF.Max(0, rads) + noise) / MaximumRads;
        }

        private void Publish() => _node.Transform = new(new Basis(Vector3.Up, -_position).Scaled(_rest.Basis.Scale), _rest.Origin);
        internal void Restore()
        {
            if (GodotObject.IsInstanceValid(_node)) _node.Transform = _rest;
        }
    }

    /// <summary>The rendered-menu tab selector rotates the source node, not its vertices.</summary>
    private sealed class TabKnobMotion
    {
        private readonly Node3D _node;
        private readonly Transform3D _rest;
        private readonly float _minimum, _maximum, _rate;
        private float _position, _target;
        internal object State => new
        {
            positionRadians = _position,
            targetRadians = _target,
            radiansPerMillisecond = _rate,
            source = "owned-TabKnob;owned-Pipboy-INI;five-tab-linear-pursuit",
            retailTiming = "unverified"
        };

        internal TabKnobMotion(NativeOwnedDeviceSurface surface, FalloutInstallationSettings settings)
        {
            _node = surface.Root.FindChildren("*", "", true, false).OfType<Node3D>()
                .Single(node => node.GetMeta("opennv_nif_source_name", "").AsString() == "TabKnob");
            _rest = _node.Transform;
            _minimum = settings.Number("Pipboy", "fTabKnobMinPosition");
            _maximum = settings.Number("Pipboy", "fTabKnobMaxPosition");
            _rate = settings.Number("Pipboy", "fTabKnobMoveRate");
            if (!float.IsFinite(_minimum) || !float.IsFinite(_maximum) || _maximum < _minimum ||
                !float.IsFinite(_rate) || _rate < 0)
                throw new InvalidDataException("Owned Pip-Boy tab knob settings are invalid.");
            _position = _target = _minimum;
            Publish();
        }

        internal void Advance(double delta, int tab)
        {
            if (!double.IsFinite(delta) || delta < 0 || tab is < 0 or > 4)
                throw new InvalidDataException("Pip-Boy tab knob received an invalid clock or tab.");
            // The menu defines five evenly spaced stops. Native knob rates use
            // menu-clock milliseconds; preserve capped linear motion in either
            // direction, including a target change while already in motion.
            _target = _minimum + (_maximum - _minimum) * tab / 4;
            _position = Mathf.MoveToward(_position, _target, (float)(delta * 1000 * _rate));
            Publish();
        }

        private void Publish()
        {
            // Native axis-angle transforms rotate clockwise about source Z,
            // which maps to Godot Y. The owner replaces local rotation while
            // retaining the authored pivot/translation and uniform scale.
            _node.Transform = new(new Basis(Vector3.Up, -_position).Scaled(_rest.Basis.Scale), _rest.Origin);
        }

        internal void Restore()
        {
            if (GodotObject.IsInstanceValid(_node)) _node.Transform = _rest;
        }
    }
}
