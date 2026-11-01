using System.Globalization;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedRecipeMenu : Control
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPlayerInventory _inventory;
    private readonly IReadOnlyList<FalloutRecipe> _recipes;
    private readonly Func<FalloutCondition, float> _evaluateCondition;
    private readonly Func<int, float> _skillValue;
    private readonly Action<FalloutRecipe, int, long> _craft;
    private readonly Action _close;
    private readonly FalloutRecipeCategory _category;
    private readonly NativeOwnedMenuTree _tiles;
    private readonly XElement _template, _recipeList, _componentList, _title, _accept, _icon, _data;
    private readonly List<(XElement Tile, NativeOwnedTileTarget Control)> _actions = [];
    private readonly FalloutMenuPcButtonBindings<Key> _shortcuts;
    private readonly Dictionary<XElement, Action> _actionOwners = [];
    private readonly IReadOnlyList<FalloutRecipeCategory> _filters;
    private readonly IReadOnlyDictionary<int, string> _skillNames;
    private NativeViewportLayout? _viewportLayout;
    private NativeOwnedQuantityMenu? _quantity;
    private FalloutRecipe? _selected;
    private int _filter = -1, _displayedMaximum;
    private long _revision = -1;
    private bool _closed, _building;
    internal bool QuantityOpen => _quantity is not null;
    internal string? Error { get; private set; }
    internal string? ActionError { get; private set; }
    internal object State => new
    {
        source = "menus/recipe_menu.xml",
        selected = _selected?.Record.FormKey.ToString(),
        category = _category.Form.ToString(),
        filter = _filter < 0 ? null : _filters[_filter].Form.ToString(),
        maximum = _displayedMaximum,
        inventoryRevision = _revision,
        observation = "last-source-UI-binding",
        quantity = _quantity?.Quantity,
        quantityError = _quantity?.Error,
        error = Error,
        actionError = ActionError,
        unbound = "item-stat-publication;non-C-name-collation;native-sort-ties;controller-and-XR-input;focus-sounds;menu-fades;matched-pixels"
    };

    internal NativeOwnedRecipeMenu(FalloutPluginStack records, FalloutPlayerInventory inventory,
        FalloutRecipeCategory category, IReadOnlyList<FalloutRecipe> recipes,
        Func<FalloutCondition, float> evaluateCondition, Func<int, float> skillValue,
        Action<FalloutRecipe, int, long> craft, Action close)
    {
        Name = "OwnedRecipeMenu"; ProcessMode = ProcessModeEnum.Always; MouseFilter = MouseFilterEnum.Stop;
        _records = records; _inventory = inventory; _category = category; _recipes = recipes;
        _evaluateCondition = evaluateCondition; _skillValue = skillValue; _craft = craft; _close = close;
        var source = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/recipe_menu.xml")).Elements("menu").Single();
        _tiles = new(source, name => FalloutGameSettingStrings.Read(records, name));
        XElement Named(string name, string? kind = null) => source.DescendantsAndSelf().Single(tile =>
            (string?)tile.Attribute("name") == name && (kind is null || tile.Name == kind));
        _template = new(Named("RM_list_template").Elements().Single());
        _recipeList = Named("RM_Items_InventoryList"); _componentList = Named("RM_Items_IngredientList", "hotrect");
        _title = Named("RM_ItemsTitle"); _accept = Named("RM_ButtonX"); _icon = Named("RM_ItemIcon"); _data = Named("RM_ItemData");
        _filters = recipes.Where(recipe => recipe.Subcategory is not null).Select(recipe => recipe.Subcategory!.Value)
            .Distinct().Select(form => FalloutRecipeCategory.Read(records, form)).OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        _skillNames = FalloutNativeTagSkillResolver.ResolveSkills(records).ToDictionary(skill =>
            (int)FalloutActorValueNames.Parse(FalloutNativeTagSkillResolver.ActorValueName(records, skill)), skill => skill.DisplayName);
        _tiles.Bind(source, "_IsContainerListSelected", 0);
        _tiles.Bind(_data, "visible", 0); // The separate generic native stat formatter remains unbound.
        _tiles.Bind(_icon, "visible", 0);
        foreach (var (tile, action) in new[]
        {
            (Named("RM_Items_LeftFilterArrow"), (Action)(() => Filter(-1))),
            (Named("RM_Items_RightFilterArrow"), (Action)(() => Filter(1))),
            (_accept, (Action)Accept), (Named("RM_ButtonB"), (Action)Close)
        }) AddAction(tile, action);
        _shortcuts = NativeMenuPcButtons.Bind(source, declaration =>
        {
            return declaration.Target is { } tile && _actionOwners.TryGetValue(tile, out var action) ? action :
                () => throw new NotSupportedException("Recipe shortcut has no source target owner.");
        });
        source.Elements("template").Remove();
        InitializeList(_recipeList); InitializeList(_componentList);
        SetMeta("opennv_menu_id", 1077); SetMeta("opennv_ui_source", "menus/recipe_menu.xml;RM_list_template;RCPE/RCCT;source-PC-shortcuts");
        try { Refresh(); } catch { Free(); throw; }
    }

    private (bool Visible, int Maximum, string? Failure) Available(FalloutRecipe recipe)
    {
        try
        {
            if (!FalloutCondition.AllPass(recipe.Conditions, _evaluateCondition, evaluateRunOn: true)) return (false, 0, null);
            if (recipe.RequiredSkill != -1 && _skillValue(recipe.RequiredSkill) < recipe.RequiredLevel) return (true, 0, null);
            return (true, _inventory.MaximumCraftCount(recipe), null);
        }
        catch (Exception error) { return (true, 0, error.Message); }
    }

    private void Refresh()
    {
        if (_building || _closed) return;
        _building = true; _pendingFocus = null;
        try
        {
            var available = _recipes.Where(recipe => _filter < 0 || recipe.Subcategory == _filters[_filter].Form)
                .Select(recipe => (Recipe: recipe, Admission: Available(recipe))).Where(value => value.Admission.Visible)
                .OrderBy(value => value.Admission.Maximum > 0 ? 0 : 1)
                .ThenBy(value => value.Recipe.Name, StringComparer.Ordinal).ToArray();
            if (_selected is null || !available.Any(value => value.Recipe.Record.FormKey == _selected.Record.FormKey))
                _selected = available.FirstOrDefault().Recipe;
            SetRows(_recipeList, available.Select(value => new SourceRow(
                value.Recipe.Outputs[0].Count > 1 ? value.Recipe.Name + " (" + value.Recipe.Outputs[0].Count.ToString(CultureInfo.InvariantCulture) + ")" : value.Recipe.Name,
                value.Admission.Maximum > 0 ? 255 : 127,
                () => Select(value.Recipe), () => { Select(value.Recipe); Accept(); }, value.Recipe.Record.FormKey)).ToArray());
            _tiles.Text[_title] = _filter < 0 ? FalloutGameSettingStrings.Read(_records, "sRecipes") : _filters[_filter].Name;
            BindDetails(); _revision = _inventory.Revision;
            LayoutLists(); LayoutActions(); QueueRedraw();
        }
        finally { _building = false; }
    }

    private void BindDetails()
    {
        XElement Named(string name, string? kind = null) => _tiles.Root.DescendantsAndSelf().Single(tile =>
            (string?)tile.Attribute("name") == name && (kind is null || tile.Name == kind));
        _tiles.Text[Named("RM_MadeAtVariable")] = _selected?.Category is { } category
            ? FalloutRecipeCategory.Read(_records, category).Name : "";
        if (_selected is not { } recipe)
        {
            _displayedMaximum = 0;
            SetRows(_componentList, []); _tiles.Text[Named("RM_SkillRequirement")] = "";
            _tiles.Bind(_accept, "target", 0); _tiles.Bind(_icon, "visible", 0); return;
        }
        var admission = Available(recipe); _displayedMaximum = admission.Maximum;
        if (admission.Failure is { } failure) ActionError = failure;
        _tiles.Bind(_accept, "target", admission.Maximum > 0 ? 1 : 0);
        var skill = Named("RM_SkillRequirement");
        _tiles.Text[skill] = recipe.RequiredSkill == -1 ? "" :
            (_skillNames.TryGetValue(recipe.RequiredSkill, out var name) ? name : throw new NotSupportedException("Recipe skill has no source AVIF label.")) +
            " (" + ((int)_skillValue(recipe.RequiredSkill)).ToString(CultureInfo.InvariantCulture) + "/" + recipe.RequiredLevel.ToString(CultureInfo.InvariantCulture) + ")";
        _tiles.Bind(skill, "alpha", recipe.RequiredSkill == -1 || _skillValue(recipe.RequiredSkill) >= recipe.RequiredLevel ? 255 : 127);
        var breakdown = recipe.Outputs.Count > 1 && recipe.Ingredients.Count == 1;
        _tiles.Text[Named("RM_Items_IngredientList", "text")] = breakdown && recipe.Subcategory is { } subcategory
            ? FalloutRecipeCategory.Read(_records, subcategory).Name : FalloutGameSettingStrings.Read(_records, "sIngredients");
        var components = breakdown ? recipe.Outputs : recipe.Ingredients;
        ResetList(_componentList);
        SetRows(_componentList, components.OrderBy(component => NameOf(component.Form), StringComparer.Ordinal)
            .Select(component => new SourceRow(NameOf(component.Form) + " (" +
                (breakdown ? "" : (_inventory.Item(component.Form)?.Count ?? 0).ToString(CultureInfo.InvariantCulture) + "/") +
                component.Count.ToString(CultureInfo.InvariantCulture) + ")",
                breakdown || (_inventory.Item(component.Form)?.Count ?? 0) >= component.Count ? 255 : 127,
                () => ShowIcon(component.Form), () => ShowIcon(component.Form), component.Form)).ToArray());
        ShowIcon(recipe.Outputs[0].Form);
    }

    private void ShowIcon(FalloutFormKey form)
    {
        var fields = _records.GetEffective(form).ReadSubrecords().ToArray();
        var icon = fields.SingleOrDefault(field => field.Signature == "ICON").Data;
        _tiles.Bind(_icon, "visible", icon.IsEmpty ? 0 : 1);
        if (!icon.IsEmpty) _tiles.SetFilename(_icon, FalloutDialogueTopic.Text(icon.Span));
        QueueRedraw();
    }
    private string NameOf(FalloutFormKey form)
    {
        var fields = _records.GetEffective(form).ReadSubrecords().ToArray();
        var name = fields.SingleOrDefault(field => field.Signature == "FULL").Data;
        if (name.IsEmpty) name = fields.Single(field => field.Signature == "EDID").Data;
        return FalloutDialogueTopic.Text(name.Span);
    }
    private void Select(FalloutRecipe recipe)
    {
        if (_closed || _quantity is not null) return;
        if (_selected?.Record.FormKey == recipe.Record.FormKey) return;
        _selected = recipe; ActionError = null; BindDetails(); LayoutLists(); LayoutActions(); QueueRedraw();
    }
    private void Filter(int direction)
    {
        if (_quantity is not null) return;
        _filter = (_filter + 1 + direction + _filters.Count + 1) % (_filters.Count + 1) - 1;
        _selected = null; ActionError = null; ResetList(_recipeList); Refresh();
    }
    private void Accept()
    {
        if (_closed || Error is not null || _quantity is not null || _selected is not { } recipe) return;
        var admission = Available(recipe);
        if (admission.Maximum <= 0) { ActionError = admission.Failure; return; }
        var revision = _inventory.Revision;
        _quantity = new(_records, admission.Maximum, amount =>
        {
            var dialog = _quantity; _quantity = null;
            dialog?.QueueFree();
            if (amount is null or 0 || _closed) return;
            try
            {
                var current = Available(recipe);
                if (!current.Visible || amount > current.Maximum) throw new InvalidOperationException("Crafting admission changed before quantity confirmation.");
                _craft(recipe, amount.Value, revision); ActionError = null;
            }
            catch (Exception error) { ActionError = error.Message; GD.PushError("OPENNV_RECIPE_CRAFT_UNBOUND " + error.Message); }
            Refresh();
        }, minimum: 0);
        AddChild(_quantity);
    }
    private void Close() { if (_closed) return; _closed = true; _close(); }
    public override void _EnterTree() => _viewportLayout = new(this, Layout);
    public override void _ExitTree() => _viewportLayout?.Dispose();
    public override void _Ready()
    {
        Try(Layout);
        if (_lists.FirstOrDefault()?.Rows.FirstOrDefault() is { } row)
            Callable.From(() => { if (IsInstanceValid(row.Control) && row.Control.IsInsideTree()) row.Control.GrabFocus(); }).CallDeferred();
    }
    private void Layout()
    {
        if (!IsInsideTree()) return;
        var pixels = GetViewportRect().Size; var scale = pixels.Y / 960;
        Scale = Vector2.One * scale; Size = _tiles.Screen = pixels / scale; _tiles.ResolutionConverter = 1 / scale;
        LayoutLists();
        LayoutActions();
        _tiles.ValidateDrawing(); QueueRedraw();
    }
    private void LayoutActions()
    {
        foreach (var (tile, control) in _actions)
        {
            control.Position = _tiles.Position(tile); control.Size = new(_tiles.Number(tile, "width"), _tiles.Number(tile, "height"));
            control.Disabled = _tiles.Number(tile, "target") == 0;
        }
    }
    public override void _Process(double delta)
    {
        if (_closed || Error is not null || _quantity is not null) return;
        if (_pendingFocus is { } pending && !GuiCaptureHeld)
        {
            _pendingFocus = null;
            if (pending.View.Rows.Contains(pending.Row)) Try(() => Focus(pending.View, pending.Row, pending.EnsureVisible));
        }
        if (_revision != _inventory.Revision && !GuiCaptureHeld) Try(Refresh);
    }
    private void Try(Action action)
    {
        if (_closed || Error is not null) return;
        try { action(); }
        catch (Exception error) { Error = error.Message; GD.PushError("OPENNV_RECIPE_UI_FAIL " + error.Message); }
    }
    public override void _Draw() => Try(() => _tiles.Draw(this));
    public override void _Input(InputEvent input)
    {
        if (_closed || _quantity is not null) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { Close(); GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            var handled = true;
            Try(() =>
            {
                if (_shortcuts.TryDispatch(key.Keycode, key.Pressed, key.Echo)) return;
                switch (key.Keycode)
                {
                    case Key.Escape: Close(); break;
                    case Key.Up: MoveSelection(-1); break;
                    case Key.Down: MoveSelection(1); break;
                    case Key.Left: Filter(-1); break;
                    case Key.Right: Filter(1); break;
                    case Key.Enter or Key.KpEnter: Accept(); break;
                    default: handled = false; break;
                }
            });
            if (handled) GetViewport().SetInputAsHandled();
        }
        if (input is InputEventMouseButton { Pressed: true } mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Try(() => ScrollAt(GetLocalMousePosition(), mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1));
            GetViewport().SetInputAsHandled();
        }
    }
}
