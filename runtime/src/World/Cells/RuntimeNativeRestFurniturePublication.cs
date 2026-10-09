using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// A lease of the real loaded source model, attached by the same factory which
// instantiated that model. It creates no actor, geometry, marker or procedure.
internal sealed partial class RuntimeNativeRestFurniturePublication : Node
{
    private readonly Node3D _model;
    internal FalloutSleepWaitBed Source { get; }
    private bool _published;
    private string? _failure;

    private RuntimeNativeRestFurniturePublication(Node3D model, FalloutSleepWaitBed source)
    {
        Name = "SourceBedPublication";
        _model = model; Source = source;
    }

    internal static RuntimeNativeRestFurniturePublication Attach(FalloutPluginStack records,
        FalloutPlacedReference reference, FalloutNifFile modelSource, Node3D actualModel)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(modelSource);
        ArgumentNullException.ThrowIfNull(actualModel);
        if (!GodotObject.IsInstanceValid(actualModel) || actualModel.IsQueuedForDeletion() ||
            actualModel.GetChildren().OfType<RuntimeNativeRestFurniturePublication>().Any())
            throw new InvalidOperationException("Bed publication needs one living actual model factory owner.");
        // All deterministic source/refusal work precedes native Node allocation.
        var source = FalloutSleepWaitBed.Read(records, reference, modelSource);
        var lease = new RuntimeNativeRestFurniturePublication(actualModel, source);
        try
        {
            actualModel.AddChild(lease);
            if (lease.GetParent() != actualModel)
                throw new InvalidOperationException("Bed publication was not attached to its actual model.");
            return lease;
        }
        catch
        {
            if (lease.GetParent() is { } parent) parent.RemoveChild(lease);
            lease.Free(); throw;
        }
    }

    public override void _EnterTree()
    {
        if (GetParent() != _model)
        {
            _failure ??= "Actual bed model publication was moved to another native owner.";
            GD.PushError("OPENNV_REST_BED_PUBLICATION_UNBOUND " + _failure);
            return;
        }
        _published = true;
    }

    public override void _ExitTree() => _published = false;

    internal FalloutRestObservation Observe(FalloutSleepWaitBed source)
    {
        Source.RequireCurrent(source);
        var identity = $"native-source-bed-model:{Source.Reference}:{Source.ModelSha256}";
        if (_failure is not null) return new(FalloutRestFactState.Unowned, identity, _failure);
        if (!_published || !GodotObject.IsInstanceValid(_model) || !IsInsideTree() || !_model.IsInsideTree() ||
            IsQueuedForDeletion() || _model.IsQueuedForDeletion() || GetParent() != _model)
            return new(FalloutRestFactState.Unowned, identity,
                "The actual source bed model has no current attached native publication lease.");
        return new(FalloutRestFactState.Satisfied, identity);
    }
}
