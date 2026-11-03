using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedHudMessages
{
    private XElement? _discoveryBranch, _discoveryHeader;
    private readonly List<XElement> _discoveryGlyphs = [];
    private FalloutDiscoveryHudDeclaration? _discoveryDeclaration;
    private double _discoveryFadeIn, _discoveryFadeOut, _discoveryRemain, _discoveryDelay;
    private float _discoveryOpacity;

    private void InitializeDiscoveryTiles()
    {
        _discoveryBranch = _quest.Elements().Single(element => (string?)element.Attribute("name") == "QuestAdded");
        _discoveryHeader = new XElement(_text);
        _discoveryHeader.SetAttributeValue("name", "discovery-prefix");
        _discoveryBranch.Add(_discoveryHeader);
    }

    private void EnsureDiscoveryProfile()
    {
        if (_discoveryDeclaration is not null) return;
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Discovery HUD source is absent.");
        var declaration = FalloutExecutableStringTable.ReadDiscoveryHudDeclaration(source.FalloutExecutablePath);
        double Setting(string name) => FalloutGameSettingFloats.ReadRetained(_records, name, nameof(NativeOwnedHudMessages));
        _discoveryFadeIn = Setting("fQuestCinematicCharacterFadeIn");
        _discoveryFadeOut = Setting("fQuestCinematicCharacterFadeOut");
        _discoveryRemain = Setting("fQuestCinematicCharacterRemain");
        var delay = Setting("fQuestCinematicCharacterFadeInDelay");
        _discoveryOpacity = _settings.Number("Interface", "fHudOpacity");
        if (!double.IsFinite(_discoveryFadeIn) || _discoveryFadeIn < 0 || !double.IsFinite(_discoveryFadeOut) || _discoveryFadeOut < 0 ||
            !double.IsFinite(_discoveryRemain) || _discoveryRemain <= 0 || !double.IsFinite(delay) || delay < 0 ||
            !float.IsFinite(_discoveryOpacity) || _discoveryOpacity is < 0 or > 1)
            throw new NotSupportedException("Discovery cinematic settings have no admitted clock/opacity.");
        // The source increments its integer millisecond deadline separately for
        // each character; do not round the accumulated floating-point delay.
        _discoveryDelay = Math.Truncate(delay * 1000) / 1000;
        _discoveryDeclaration = declaration;
    }

    private string DiscoveryName(string name)
    {
        EnsureDiscoveryProfile();
        // The source's default C-locale byte conversion changes ASCII a-z.
        return new string(name.Take(_discoveryDeclaration!.NameCapacity)
            .Select(character => character is >= 'a' and <= 'z' ? (char)(character - ('a' - 'A')) : character).ToArray());
    }

    private double DiscoveryDuration(string name) => DiscoveryName(name).Length * _discoveryDelay + _discoveryRemain + _discoveryFadeOut;

    private void LayoutDiscovery()
    {
        if (_queue.Current?.Event.Kind != FalloutHudEventKind.LocationDiscovered || _discoveryDeclaration is not { } declaration) return;
        var wide = Size.X / Size.Y >= 16f / 9;
        _tiles.Bind(_quest, "x", declaration.XInset + declaration.SafeZoneScale * _settings.Number("Interface", wide ? "iSafeZoneXWide" : "iSafeZoneX"));
        _tiles.Bind(_quest, "y", declaration.YInset + declaration.SafeZoneScale * _settings.Number("Interface", wide ? "iSafeZoneYWide" : "iSafeZoneY"));
    }

    private void PresentDiscovery(FalloutHudNotice notice)
    {
        EnsureDiscoveryProfile();
        var declaration = _discoveryDeclaration!;
        foreach (var glyph in _discoveryGlyphs) _tiles.Bind(glyph, "visible", 0);
        _tiles.Bind(_quest, "alpha", 255);
        _tiles.Bind(_discoveryHeader!, "font", declaration.HeaderFont);
        _tiles.Bind(_discoveryHeader!, "visible", 1);
        _tiles.Bind(_discoveryHeader!, "justify", 0);
        _tiles.Bind(_discoveryHeader!, "x", 0);
        _tiles.Bind(_discoveryHeader!, "y", -(_tiles.Font(_discoveryHeader!).Font.Height + declaration.HeaderGap));
        _tiles.Text[_discoveryHeader!] = FalloutGameSettingStrings.Read(_records, declaration.PrefixSetting);
        var name = DiscoveryName(FalloutMapMarker.Read(_records.GetEffective(notice.Event.Source)).Name);
        var wrapAt = declaration.WrapAt;
        if (name.Length >= wrapAt)
            for (var at = Math.Min(wrapAt, name.Length - 1); at >= 0; at--)
                if (name[at] == ' ') { wrapAt = at; break; }
        var x = 0f;
        for (var at = 0; at < name.Length; at++)
        {
            if (at == wrapAt + 1) x = 0;
            if (at == _discoveryGlyphs.Count)
            {
                var added = new XElement(_text);
                added.SetAttributeValue("name", $"discovery-character-{at}");
                _discoveryBranch!.Add(added); _discoveryGlyphs.Add(added);
            }
            var tile = _discoveryGlyphs[at];
            _tiles.Bind(tile, "font", declaration.NameFont);
            _tiles.Bind(tile, "visible", 1); _tiles.Bind(tile, "justify", 0);
            _tiles.Bind(tile, "x", x);
            _tiles.Bind(tile, "y", at > wrapAt ? _tiles.Font(tile).Font.Height + declaration.LineGap : 0);
            _tiles.Text[tile] = name[at].ToString();
            x += MathF.Truncate(_tiles.Font(tile).Font.Measure(_tiles.Text[tile]));
        }
        LayoutDiscovery(); AdvanceDiscovery();
        if (!notice.Presented)
        {
            var sound = FalloutSoundRecordReader.Find(_records, declaration.SoundEditorId);
            (_noteSounds ?? throw new NotSupportedException("Discovery sound has no world voice owner."))
                .Play(_records.RuntimeFormKey(0x14), sound.FormKey, systemSound: true);
            _queue.MarkPresented(notice.Ordinal);
        }
    }

    private void AdvanceDiscovery()
    {
        float Alpha(double start)
        {
            var elapsed = _queue.Elapsed - start;
            if (elapsed < 0) return 0;
            var atFadeOut = _discoveryFadeIn == 0 ? 1 : Math.Min(_discoveryRemain / _discoveryFadeIn, 1);
            var alpha = elapsed < _discoveryRemain ? 255 * _discoveryOpacity * (_discoveryFadeIn == 0 ? 1 : Math.Min(elapsed / _discoveryFadeIn, 1)) :
                _discoveryFadeOut == 0 ? 0 : Math.Max(_discoveryDeclaration!.FadeOutAlphaMinimum, 255 * _discoveryOpacity * atFadeOut) *
                    Math.Max(0, 1 - (elapsed - _discoveryRemain) / _discoveryFadeOut);
            return (float)alpha;
        }
        _tiles.Bind(_discoveryHeader!, "alpha", Alpha(0));
        for (var at = 0; at < _discoveryGlyphs.Count; at++) _tiles.Bind(_discoveryGlyphs[at], "alpha", Alpha((at + 1) * _discoveryDelay));
        QueueRedraw();
    }
}
