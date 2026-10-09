using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedSleepWaitMenu
{
    internal void PublishSourceCountingTarget(FalloutRestMenuTarget target)
    {
        RequireLivingControlProjection();
        var controls = _session.RequireCurrentMenuControls();
        if (!controls.Counting || !controls.OverridesTarget(target))
            throw new InvalidOperationException("The native menu cannot invent an uncommitted source target value.");
        if (target == FalloutRestMenuTarget.Action)
        {
            var tile = _source.Named("SWM_WaitButton"); _source.Tiles.Bind(tile, "target", 0);
            _actions.Single(action => action.Id == 4).Button.Disabled = true;
        }
        else if (target == FalloutRestMenuTarget.Slider)
        {
            _source.Tiles.Bind(_slider, "target", 0); _sliderTarget.Disabled = true; _dragging = false;
        }
        else throw new ArgumentOutOfRangeException(nameof(target));
        QueueRedraw();
    }

    private void RequireLivingControlProjection()
    {
        if (_faulted || Error is not null || _session.RequestOrdinal != _request || !IsInsideTree() ||
            !GodotObject.IsInstanceValid(this) || !_session.Published)
            throw new InvalidOperationException("Rest targets have no actual current source/native publication.");
    }

    private void RestoreCurrentSourceTargets()
    {
        var controls = _session.RequireCurrentMenuControls(); controls.RequireHealthy();
        if (controls.OverridesTarget(FalloutRestMenuTarget.Action))
            _source.Tiles.Bind(_source.Named("SWM_WaitButton"), "target", 0);
        if (controls.OverridesTarget(FalloutRestMenuTarget.Slider)) _source.Tiles.Bind(_slider, "target", 0);
    }

    private bool SourceVisible(System.Xml.Linq.XElement tile) => tile.AncestorsAndSelf()
        .Where(owner => owner.Attribute("name") is not null).All(owner => _source.Tiles.Number(owner, "visible") != 0);

    private bool SourceTargetEnabled(System.Xml.Linq.XElement tile)
    {
        var value = _source.Tiles.Number(tile, "target");
        if (!float.IsFinite(value)) throw new InvalidDataException("Rest source target is non-finite.");
        return value != 0;
    }
}
