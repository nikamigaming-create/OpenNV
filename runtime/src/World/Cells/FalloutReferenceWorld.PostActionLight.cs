using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutProcessSceneQuery? _enteredPostActionLight;
    private FalloutSceneDirectionalFields? _sceneDirectionalLight;
    private FalloutProcessSceneQuery ReadReferencePositionCommon(FalloutFormKey actor, long epoch) => _enteredPostActionLight is null
        ? ProcessCommon.ReadSceneQueries(actor, epoch) : ProcessCommon.ReadPostActionLightBorrow(actor, epoch);
    internal void StoreSceneCellLighting(FalloutCellDefinition cell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lighting = cell.Lighting ?? throw new NotSupportedException("source-scene-current-cell-directional-fields-unbound");
        static float[] Rgb(byte[] color) => color.Select(value => value / 255f).ToArray();
        _sceneDirectionalLight = new(cell.FormKey, Rgb(lighting.AmbientRgb), Rgb(lighting.DirectionalRgb), lighting.DirectionalFade);
    }
    private FalloutProcessPostActionLightCall ReadStandalonePostActionLight(FalloutProcessPostActionLightCall call,
        FalloutSourceSharedShaderProperty shader, Action<FalloutProcessPostActionLightCall> publish)
    {
        if (_enteredPostActionLight is not null || call.Query.Actor.Reference != _enginePlayer ||
            !call.Query.Actor.EnginePlayer || call.Shader != shader.Source)
            throw new InvalidOperationException("Player light sampling changed its actual caller or shader.");
        _enteredPostActionLight = call.Query;
        try
        {
            if (call.FirstLight is null)
            {
                Step("current-first-model-getter");
                var model = (_playerFirstModel ?? throw new NotSupportedException("source-light-first-model-current-field-unbound")).ReadCurrent();
                call = call with { ModelRead = true, FirstModel = model }; publish(call);
                Step("shader-second-light-getter");
                call = call with { SecondGetterReturned = true, SecondLight = shader.ReadFirstLight() }; publish(call);
            }
            Step("current-reference-position");
            var position = WithActualReferencePosition(FalloutReferencePositionIngress.CopyOrLoad,
                invocation => _sourcePlayerReferencePosition!.ReadCurrentPosition(invocation));
            call = call with { Position = position }; publish(call);
            Step("current-maximum-BBX"); var maximum = ReadBounds(); call = call with { MaximumBounds = maximum }; publish(call);
            Step("current-minimum-BBX"); var minimum = ReadBounds(); call = call with { MinimumBounds = minimum }; publish(call);
            var sourcePosition = new[] { BitConverter.UInt32BitsToSingle(position.Position.X),
                BitConverter.UInt32BitsToSingle(position.Position.Y), BitConverter.UInt32BitsToSingle(position.Position.Z) };
            var sample = FalloutDetectionLight.SamplePosition(sourcePosition, minimum?.Minimum[2] ?? 0, maximum?.Maximum[2] ?? 0);
            call = call with { SamplePosition = sample }; publish(call);
            Step("current-scene-point-lights"); FalloutScenePointLightSum points;
            try { points = _sceneLights.Sum(sample); }
            finally { call = call with { Points = _sceneLights.LastSum }; publish(call); }
            Step("current-scene-directional-fields");
            var directional = _sceneDirectionalLight ?? throw new NotSupportedException("source-scene-current-directional-light-owner-unbound");
            call = call with { Directional = directional }; publish(call);
            Step("current-ParentCELL"); var parent = ReadReferenceModelParent(); call = call with { Parent = parent }; publish(call);
            var interior = parent.Cell is { } cell && (FalloutCellSceneReader.ReadDefinition(records, cell).Flags & 1) != 0;
            if (interior && parent.Cell != directional.Cell)
                throw new NotSupportedException("source-scene-current-interior-directional-cell-transfer-unowned");
            var sum = BitConverter.UInt32BitsToSingle(points.SumBits);
            float additional;
            if (interior) additional = directional.Ambient.Max() + directional.Diffuse.Max();
            else additional = FalloutDetectionLight.Contribution(new([0, 0, 0], directional.Diffuse,
                directional.Dimmer, 0, false, false, false), sample);
            var value = (float)((double)sum + additional) * 100f;
            call = call with { Step = "returned", ValueBits = BitConverter.SingleToUInt32Bits(value), Returned = true };
            publish(call); return call;
        }
        finally { _enteredPostActionLight = null; }
        void Step(string step) { call = call with { Step = step }; publish(call); }
        FalloutDetectionBounds? ReadBounds()
        {
            // The inherited getter's constructor-null process cache falls back
            // to the same current Reference model and its actual BBX extra.
            var model = (_sourcePlayerModelData ?? throw new NotSupportedException("source-light-reference-model-owner-unbound")).ReadForLight(call.Query);
            var cache = CampaignStandalonePlayerScene.ThreadContext.ReadPlayerModelCache(_enginePlayer);
            if (cache.Matched && cache.Model != model?.Node)
                throw new NotSupportedException("source-light-current-thread-model-BBX-owner-unbound");
            return model is null ? null : FalloutDetectionBounds.Read(model.LogicalScene.SourceModel.Skeleton.Source,
                model.LogicalScene.Root.Key.Block);
        }
    }
}
