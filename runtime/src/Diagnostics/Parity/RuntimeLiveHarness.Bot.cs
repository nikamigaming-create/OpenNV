using Godot;
using OpenNV.Runtime.Gameplay.Bots;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private readonly VerifiedBotSkillLibrary _botSkills = new();
    private BotCombatBindings? _botCombatBindings;
    private bool _botSkillsLoaded, _botSkillsDirty;
    private string? _botSkillsError;
    private BotPhysicalInput? _botAimInput;
    private readonly record struct BotPhysicalInput(Key Key, MouseButton Mouse);
    private object BotSkillStoreState => new
    {
        path = Path.Combine(_directory, "bot-skills.json"),
        loaded = _botSkillsLoaded,
        pendingPersistence = _botSkillsDirty,
        error = _botSkillsError,
        library = _botSkills.State
    };

    private void EnsureBotSkillLibrary()
    {
        if (_botSkillsLoaded)
        {
            if (_botSkillsDirty) PersistBotSkillLibrary();
            return;
        }
        var path = Path.Combine(_directory, "bot-skills.json");
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 1_048_576) throw new InvalidDataException("Bot evidence library exceeds its bounded extent.");
            if (!LiveHarnessAtomicFile.TryRead(path, out var json, out var error))
                throw new IOException("Bot evidence library is unavailable: " + error);
            _botSkills.Restore(json);
        }
        _botSkillsLoaded = true;
    }

    private void PersistBotSkillLibrary()
    {
        _botSkillsDirty = true;
        try
        {
            AtomicWrite(Path.Combine(_directory, "bot-skills.json"), _botSkills.Serialize());
            _botSkillsDirty = false; _botSkillsError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { _botSkillsError = error.Message; throw; }
    }

    private void DeliverBotCombatInput(SteeringIntent intent)
    {
        var combat = intent.Combat;
        if (combat?.Aim == true)
        {
            var desired = PhysicalBotAction((_botCombatBindings ??
                throw new NotSupportedException("Ordinary combat input bindings are absent.")).AimAction);
            if (_botAimInput != desired)
            {
                ReleaseBotCombatInput();
                _botAimInput = desired; DeliverPhysicalBotInput(desired, true);
            }
            else if (desired.Key != Key.None) SetKey(desired.Key, true, 200);
        }
        else ReleaseBotCombatInput();
        if (combat?.Fire == true) PulseBotAction(_botCombatBindings!.FireAction);
        if (combat?.Reload == true) PulseBotAction(_botCombatBindings!.ReloadAction);
        if (intent.Pause)
        {
            ReleaseBotCombatInput();
            PulseBotAction((_botCombatBindings ?? throw new NotSupportedException("Ordinary pause input binding is absent.")).PauseAction);
        }
    }

    private BotPhysicalInput PhysicalBotAction(string action)
    {
        if (!InputMap.HasAction(action)) throw new NotSupportedException("Ordinary bot action is unbound: " + action);
        foreach (var input in InputMap.ActionGetEvents(action))
        {
            if (input is InputEventKey key && (key.PhysicalKeycode != Key.None || key.Keycode != Key.None))
                return new(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode, MouseButton.None);
            if (input is InputEventMouseButton mouse && mouse.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
                return new(Key.None, mouse.ButtonIndex);
        }
        throw new NotSupportedException("Ordinary bot action has no native keyboard/mouse binding: " + action);
    }

    private void PulseBotAction(string action)
    {
        var physical = PhysicalBotAction(action);
        try { DeliverPhysicalBotInput(physical, true); }
        finally { DeliverPhysicalBotInput(physical, false); }
    }

    private void DeliverPhysicalBotInput(BotPhysicalInput input, bool pressed)
    {
        if (input.Key != Key.None) SetKey(input.Key, pressed, 100);
        else
        {
            var position = GetViewport().GetVisibleRect().GetCenter();
            DeliverPointerButton(position, input.Mouse, pressed);
        }
    }

    private void ReleaseBotCombatInput()
    {
        if (_botAimInput is not { } physical) return;
        _botAimInput = null;
        DeliverPhysicalBotInput(physical, false);
    }
}
