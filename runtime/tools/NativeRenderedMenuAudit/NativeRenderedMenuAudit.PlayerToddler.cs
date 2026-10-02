using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private void PlayerToddler(string baseRoot, string mod, string root, string questId, string[] dependencies)
    {
        RuntimeNativePlayer? player = null;
        try
        {
            var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
            RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
            var hash = SHA256.HashData(quest.ReadData());
            var fields = quest.ReadSubrecords().ToArray();
            var session = new FalloutScriptSession();
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, effect =>
            {
                switch (effect.Kind)
                {
                    case FalloutReferenceEffectKind.PlayerToddler: session.SetPlayerToddler(effect.Enable); break;
                    case FalloutReferenceEffectKind.PlayerYouth: session.SetPlayerYoung(effect.Enable); break;
                    case FalloutReferenceEffectKind.PlayerScale: session.SetPlayerScale(effect.Scale); break;
                    default: throw new InvalidDataException("Toddler fixture invented a stage effect.");
                }
            }));
            void Execute(string command)
            {
                var index = Array.FindIndex(fields, field => field.Signature == "SCTX" &&
                    FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)).Contains(command));
                if (index < 0) throw new InvalidDataException("Toddler command is absent from the selected source quest.");
                var begin = index; while (begin > 0 && fields[begin].Signature != "QSDT") --begin;
                if (fields[begin].Signature != "QSDT") throw new InvalidDataException("Toddler command has no source stage entry.");
                var end = begin + 1; while (end < fields.Length && fields[end].Signature is not ("QSDT" or "INDX" or "QOBJ")) ++end;
                executor.ExecuteStage(quest, fields[begin..end], command);
            }
            var commands = fields.Where(field => field.Signature == "SCTX").SelectMany(field =>
                FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)))
                .Where(line => FalloutGameModeProgram.Tokens(line)[0].Equals("SetPCToddler", StringComparison.OrdinalIgnoreCase) ||
                    FalloutGameModeProgram.Tokens(line)[0].Equals("player.setscale", StringComparison.OrdinalIgnoreCase)).ToArray();
            var set = commands.First(line => FalloutGameModeProgram.Tokens(line)[0].Equals("SetPCToddler", StringComparison.OrdinalIgnoreCase));
            var clear = commands.Last(line => FalloutGameModeProgram.Tokens(line)[0].Equals("SetPCToddler", StringComparison.OrdinalIgnoreCase));
            var scale = commands.First(line => FalloutGameModeProgram.Tokens(line)[0].Equals("player.setscale", StringComparison.OrdinalIgnoreCase));
            Execute(set); Execute(scale);
            var cold = new FalloutScriptSession(); cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!);
            if (!cold.PlayerToddler || cold.PlayerScale != session.PlayerScale) throw new InvalidDataException("Owned toddler policy did not restore cold.");
            var configuration = RuntimeConfiguration.Load();
            var contract = FalloutNativeRaceSexResolver.Resolve(records);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var samples = 0;
            foreach (var female in new[] { false, true })
            {
                var appearance = FalloutNpcAppearanceResolver.Resolve(records, contract.Player, equippedArmor: [],
                    appearanceState: FalloutNativeCharacterCreation.ActorState(records, contract.Player, contract.ForSex(female)) with { PlayerYoung = true });
                var body = new RuntimeNativePlayerActor(records, content, appearance, null, true, configuration.World.GameUnitsToMeters, new Color(.4f, .4f, .4f));
                var third = new RuntimeNativePlayerActor(records, content, appearance, null, false, configuration.World.GameUnitsToMeters, new Color(.4f, .4f, .4f));
                try
                {
                    AddChild(body); AddChild(third); body.SetToddler(cold.PlayerToddler);
                    var idleCamera = body.SourceCamera;
                    foreach (var direction in new[] { Vector3.Forward, Vector3.Back, Vector3.Left, Vector3.Right })
                        foreach (var speed in new[] { .1f, 100f })
                            foreach (var grounded in new[] { true, false })
                            {
                                body.Advance(.233, direction * speed, grounded, false);
                                var state = JsonSerializer.SerializeToElement(body.State);
                                var path = state.GetProperty("movement").GetString()!;
                                if (body.Error is not null || !path.StartsWith("locomotion/toddler/", StringComparison.Ordinal) ||
                                    grounded && !path.EndsWith("_toddler", StringComparison.Ordinal) || !body.SourceCamera.Origin.IsFinite())
                                    throw new InvalidDataException(body.Error ?? "Toddler selection did not use its source movement/camera palette.");
                                paths.Add(path); ++samples;
                            }
                    if (body.SourceCamera.IsEqualApprox(idleCamera) || third.Toddler || third.Error is not null)
                        throw new InvalidDataException("Toddler camera did not sample owned motion or changed third-person policy.");
                    body.SetToddler(false); body.Advance(.233, Vector3.Forward, true, false);
                    if (body.Error is not null || body.Toddler || JsonSerializer.SerializeToElement(body.State).GetProperty("movement").GetString()!.Contains("toddler", StringComparison.Ordinal))
                        throw new InvalidDataException("Clearing toddler mode retained the toddler animation palette.");
                }
                finally { body.Free(); third.Free(); }
            }
            player = new(); player.Configure(configuration, Transform3D.Identity); AddChild(player);
            var playerAppearance = FalloutNpcAppearanceResolver.Resolve(records, contract.Player, equippedArmor: [],
                appearanceState: FalloutNativeCharacterCreation.ActorState(records, contract.Player, contract.ForSex(false)) with { PlayerYoung = true });
            player.ConfigurePresentation(records, new(), () => playerAppearance, () => new Color(.4f, .4f, .4f), playerPolicy: () => cold);
            player.ApplySourceControls(new(false, false, false, false, true, false, false));
            player._Process(0);
            var presented = JsonSerializer.SerializeToElement(player.PresentationState);
            var firstView = player.GetChildren().OfType<SubViewport>().Single();
            var firstBody = firstView.GetChildren().OfType<RuntimeNativePlayerActor>().Single();
            if (presented.GetProperty("error").ValueKind != JsonValueKind.Null || !presented.GetProperty("first").GetProperty("toddler").GetBoolean() ||
                !player.GetChildren().OfType<Camera3D>().Single().Transform.IsEqualApprox(firstBody.SourceCamera))
                throw new InvalidDataException("Looking-only flat presentation did not consume the shared toddler camera policy.");
            player.ApplySourceScale(cold.PlayerScale);
            player.Teleport(new(new Basis(Vector3.Up, .7f), new Vector3(2, 3, 4)));
            if (!player.GlobalBasis.Scale.IsEqualApprox(Vector3.One * cold.PlayerScale)) throw new InvalidDataException("MoveTo discarded source player scale.");
            player.RestoreTransform([2, 3, 4], [0, 0, 0, 1]);
            if (!player.GlobalBasis.Scale.IsEqualApprox(Vector3.One * cold.PlayerScale)) throw new InvalidDataException("Transform restoration discarded source player scale.");
            cold.SetPlayerToddler(false); cold.SetPlayerScale(1); player._Process(0);
            if (firstBody.Toddler || !player.GlobalBasis.Scale.IsEqualApprox(Vector3.One)) throw new InvalidDataException("Retained flat presentation did not consume cleared shared player policy.");
            var adultCamera = player.GetChildren().OfType<Camera3D>().Single().Transform;
            if (adultCamera.IsEqualApprox(firstBody.SourceCamera))
                throw new InvalidDataException("Looking-only presentation retained its cleared toddler camera.");
            Execute(clear);
            if (session.PlayerToddler || !SHA256.HashData(quest.ReadData()).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Source clear changed owned bytes or retained the toddler flag.");
            GD.Print("OPENNV_NATIVE_PLAYER_TODDLER_PASS " + JsonSerializer.Serialize(new
            {
                quest = quest.FormKey,
                commands,
                paths,
                samples,
                maleAndFemale = true,
                firstPersonSourceCamera = true,
                thirdPersonPreserved = true,
                clearedPalette = true,
                cold = true,
                scale = session.PlayerScale,
                lookingOnlyCamera = true,
                moveScaleRetained = true,
                restoredScale = true,
                sourceReadonly = true,
                recording = false,
                boundary = "isolated-owned-policy-body-and-movement;gurney-childhood-campaign-retail-XR-acceptance-unverified"
            }));
        }
        finally { player?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
