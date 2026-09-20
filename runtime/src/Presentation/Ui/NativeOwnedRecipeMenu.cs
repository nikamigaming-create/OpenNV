using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Source-category recipe choices over the shared player inventory.</summary>
internal sealed partial class NativeOwnedRecipeMenu : Control
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPlayerInventory _inventory;
    private readonly IReadOnlyList<FalloutRecipe> _recipes;
    private readonly Func<FalloutCondition, float> _evaluateCondition;
    private readonly Func<int, float> _skillValue;
    private readonly Action<FalloutRecipe> _craft;
    private readonly Action _close;
    private readonly FalloutRecipeCategory _category;
    private readonly VBoxContainer _rows;
    private readonly HFlowContainer _subcategories;
    private readonly Label _details, _message;
    private readonly Button _craftButton;
    private readonly List<Button> _recipeButtons = [];
    private FalloutRecipe? _selected;
    private FalloutFormKey? _subcategory;
    private bool _allSubcategories = true, _closed;

    internal NativeOwnedRecipeMenu(FalloutPluginStack records, FalloutPlayerInventory inventory,
        FalloutRecipeCategory category, IReadOnlyList<FalloutRecipe> recipes,
        Func<FalloutCondition, float> evaluateCondition, Func<int, float> skillValue,
        Action<FalloutRecipe> craft, Action close)
    {
        Name = "OwnedRecipeMenu";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        _records = records; _inventory = inventory; _category = category; _recipes = recipes;
        _evaluateCondition = evaluateCondition; _skillValue = skillValue; _craft = craft; _close = close;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect
        {
            Name = "Backdrop",
            Color = new(0.015f, 0.02f, 0.03f, 0.88f),
            MouseFilter = MouseFilterEnum.Stop,
            AnchorRight = 1,
            AnchorBottom = 1
        });
        var center = new CenterContainer { Name = "Center" };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { Name = "RecipePanel", CustomMinimumSize = new(900, 700) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new(0.035f, 0.045f, 0.055f, 0.98f),
            BorderColor = new(0.64f, 0.74f, 0.54f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 24,
            ContentMarginTop = 20,
            ContentMarginRight = 24,
            ContentMarginBottom = 20
        });
        center.AddChild(panel);
        var column = new VBoxContainer { Name = "RecipeColumn", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);
        var title = new Label
        {
            Name = "RecipeTitle",
            Text = category.Name,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        column.AddChild(title);
        _subcategories = new() { Name = "RecipeSubcategories", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_subcategories);
        var body = new HBoxContainer { Name = "RecipeBody", SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        column.AddChild(body);
        var listPanel = new PanelContainer { CustomMinimumSize = new(450, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(listPanel);
        var scroll = new ScrollContainer { Name = "RecipeScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        listPanel.AddChild(scroll);
        _rows = new VBoxContainer { Name = "RecipeRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rows);
        var detailPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(detailPanel);
        var detailColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detailColumn.AddThemeConstantOverride("separation", 12);
        detailPanel.AddChild(detailColumn);
        _details = new Label { Name = "RecipeDetails", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = SizeFlags.ExpandFill };
        detailColumn.AddChild(_details);
        _message = new Label { Name = "RecipeMessage", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        detailColumn.AddChild(_message);
        _craftButton = new Button { Name = "CraftSelected", Text = "Craft", CustomMinimumSize = new(0, 48), FocusMode = FocusModeEnum.All };
        _craftButton.Pressed += CraftSelected;
        detailColumn.AddChild(_craftButton);
        var closeButton = new Button { Name = "CloseRecipeMenu", Text = "Close", CustomMinimumSize = new(0, 42), FocusMode = FocusModeEnum.All };
        closeButton.Pressed += Close;
        column.AddChild(closeButton);
        BuildSubcategoryFilters();
        Refresh();
        SetMeta("opennv_ui_source", "RCPE-and-RCCT; shared-native-recipe-menu");
        SetMeta("opennv_ui_unverified", "matched-menu-pixels,retail-input-timing,physical-headset-acceptance");
    }

    private void BuildSubcategoryFilters()
    {
        AddFilter("All", null, all: true);
        foreach (var form in _recipes.Select(recipe => recipe.Subcategory).Where(form => form is not null)
                     .Select(form => form!.Value).Distinct())
        {
            var category = FalloutRecipeCategory.Read(_records, form);
            if (!category.IsSubcategory) throw new InvalidDataException($"Recipe subcategory {form} is marked as a category.");
            AddFilter(category.Name, form, all: false);
        }
        if (_recipes.Any(recipe => recipe.Subcategory is null)) AddFilter("Other", null, all: false);
    }

    private void AddFilter(string label, FalloutFormKey? form, bool all)
    {
        var button = new Button { Text = label, FocusMode = FocusModeEnum.All };
        button.Pressed += () =>
        {
            _subcategory = form; _allSubcategories = all; _selected = null; _message.Text = ""; Refresh();
        };
        _subcategories.AddChild(button);
    }

    private void Refresh()
    {
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        _recipeButtons.Clear();
        var filtered = _recipes.Where(recipe => _allSubcategories || recipe.Subcategory == _subcategory).ToArray();
        var visible = filtered.Select(recipe => (Recipe: recipe, Availability: Availability(recipe)))
            .Where(value => value.Availability.Visible).ToArray();
        if (_selected is null || !visible.Any(value => value.Recipe.Record.FormKey == _selected.Record.FormKey))
            _selected = visible.Select(value => (FalloutRecipe?)value.Recipe).FirstOrDefault();
        foreach (var item in visible)
        {
            var recipe = item.Recipe;
            var status = item.Availability.CanCraft ? "" : "  —  " + item.Availability.Status;
            var button = new Button
            {
                Name = "Recipe_" + recipe.Record.FormKey,
                Text = recipe.Name + status,
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.All,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            button.Pressed += () => Select(recipe);
            _rows.AddChild(button); _recipeButtons.Add(button);
        }
        if (visible.Length == 0)
        {
            _rows.AddChild(new Label { Text = filtered.Length == 0 ? "No recipes in this category." : "No recipes meet their visibility requirements." });
            _selected = null;
        }
        else if (_selected is { } selected) Select(selected);
        else ShowSelected();
    }

    private (bool Visible, bool CanCraft, string Status) Availability(FalloutRecipe recipe)
    {
        try
        {
            if (!FalloutCondition.AllPass(recipe.Conditions, _evaluateCondition, evaluateRunOn: true))
                return (false, false, "requirements not met");
        }
        catch (Exception error)
        {
            return (true, false, "requirement unbound: " + error.Message);
        }
        if (recipe.RequiredSkill != -1)
        {
            try
            {
                var value = _skillValue(recipe.RequiredSkill);
                if (value < recipe.RequiredLevel) return (true, false, $"requires skill {recipe.RequiredLevel}");
            }
            catch (Exception error) { return (true, false, "skill unbound: " + error.Message); }
        }
        return _inventory.CanCraft(recipe) ? (true, true, "ready") : (true, false, "missing ingredients");
    }

    private void Select(FalloutRecipe recipe)
    {
        _selected = recipe;
        ShowSelected();
    }

    private void ShowSelected()
    {
        if (_selected is not { } recipe)
        {
            _details.Text = "Select a recipe."; _craftButton.Disabled = true; return;
        }
        var availability = Availability(recipe);
        var requirement = recipe.RequiredSkill == -1 ? "No skill requirement" :
            SkillRequirement(recipe);
        var lines = new List<string> { recipe.Name, "", requirement, "", "Ingredients:" };
        lines.AddRange(recipe.Ingredients.Count == 0 ? ["  None"] : recipe.Ingredients.Select(item =>
            $"  {NameOf(item.Form)}  {_inventory.Item(item.Form)?.Count ?? 0} / {item.Count}"));
        lines.AddRange(["", "Produces:"]);
        lines.AddRange(recipe.Outputs.Select(item => $"  {NameOf(item.Form)}  x{item.Count}"));
        _details.Text = string.Join('\n', lines);
        _message.Text = availability.CanCraft ? "" : availability.Status;
        _craftButton.Disabled = !availability.CanCraft;
    }

    private string SkillRequirement(FalloutRecipe recipe)
    {
        try { return $"{SkillName(recipe.RequiredSkill)}: {recipe.RequiredLevel} required (you have {_skillValue(recipe.RequiredSkill):0})"; }
        catch (Exception) { return $"{SkillName(recipe.RequiredSkill)}: {recipe.RequiredLevel} required (current value unavailable)"; }
    }

    private static string SkillName(int skill) => skill switch
    {
        32 => "Barter",
        33 => "Big Guns",
        34 => "Energy Weapons",
        35 => "Explosives",
        36 => "Lockpick",
        37 => "Medicine",
        38 => "Melee Weapons",
        39 => "Repair",
        40 => "Science",
        41 => "Guns",
        42 => "Sneak",
        43 => "Speech",
        44 => "Survival",
        45 => "Unarmed",
        _ => $"Actor value {skill}"
    };

    private string NameOf(FalloutFormKey form)
    {
        var record = _records.GetEffective(form);
        var full = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
        if (!full.IsEmpty) return FalloutDialogueTopic.Text(full.Span);
        return FalloutDialogueTopic.Text(record.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span);
    }

    private void CraftSelected()
    {
        if (_selected is not { } recipe) return;
        var availability = Availability(recipe);
        if (!availability.CanCraft) { _message.Text = availability.Status; Refresh(); return; }
        try
        {
            _craft(recipe);
            Refresh();
            _message.Text = $"Crafted {recipe.Name}.";
        }
        catch (Exception error)
        {
            _message.Text = "Craft failed: " + error.Message;
            GD.PushError($"OPENNV_RECIPE_CRAFT_UNBOUND recipe={recipe.Record.FormKey} {error.Message}");
            Refresh();
        }
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _close();
    }

    public override void _Ready()
    {
        if (_recipeButtons.Count != 0) Callable.From(_recipeButtons[0].GrabFocus).CallDeferred();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Close(); GetViewport().SetInputAsHandled();
        }
    }
}
