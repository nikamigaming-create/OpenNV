using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;


using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class OpeningQuestRuntime
{
    private void ShowNameMenu(Action completed)
    {
        if (!_flow.Menus.TryGetValue("name", out var nameMenu) ||
            nameMenu.TextEditMenu is not { } source)
            throw new InvalidOperationException(
                "Owned TextEditMenu tile contract is absent.");
        var content = OpenOwnedTilePanel(source.Panel, "name");
        var prompt = NewLabel("");
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        OwnedGamebryoTileRuntime.BindText(prompt, source.Prompt.Text);
        var promptSize = prompt.GetCombinedMinimumSize();
        OwnedGamebryoTileRuntime.ApplyTraitPosition(
            prompt,
            source.Prompt.Placement,
            source.Panel.Rect.Size,
            promptSize);
        content.AddChild(prompt);
        var input = new LineEdit
        {
            Text = _playerName,
            PlaceholderText = source.Prompt.Text.Text,
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(
                source.InputWrapWidth,
                _opening.Font.LineHeightPixels * 2.0f),
        };
        ApplyTextTheme(input);
        var inputStyle = OwnedUiTheme.HighlightedStyle(
            _opening.MainMenuColor,
            _opening.Style);
        input.AddThemeStyleboxOverride("normal", inputStyle);
        input.AddThemeStyleboxOverride("focus", inputStyle);
        OwnedGamebryoTileRuntime.ApplyTraitPosition(
            input,
            source.Input,
            source.Panel.Rect.Size,
            input.CustomMinimumSize);
        content.AddChild(input);
        var accept = NewButton("");
        OwnedGamebryoTileRuntime.BindText(accept, source.Accept.Text);
        var acceptSize = accept.GetCombinedMinimumSize();
        OwnedGamebryoTileRuntime.ApplyTraitPosition(
            accept,
            source.Accept.Placement,
            source.Panel.Rect.Size,
            acceptSize);
        content.AddChild(accept);
        void Submit()
        {
            var value = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;
            _playerName = value;
            GD.Print($"OPENNV_NEW_GAME_NAME_CONFIRMED name={_playerName}");
            CloseModal();
            completed();
        }
        accept.Pressed += Submit;
        input.TextSubmitted += _ => Submit();
        Callable.From(input.GrabFocus).CallDeferred();
        GD.Print("OPENNV_NEW_GAME_NAME_INPUT_READY visible=true focus=deferred");
    }

    private void ShowAppearanceMenu(Action completed)
    {
        var appearance = _flow.Character.Appearance;
        var faceGen = appearance.FaceGen;
        var previewPolicy = faceGen.ControlSpace.PreviewControl;
        var appearanceMenu = _flow.Menus["appearance"];
        if (appearanceMenu.RaceSexMenuTiles is not { } source ||
            appearanceMenu.Rect != source.Background.Rect ||
            !_flow.Strings.TryGetValue(
                source.Navigation.Back.LabelRole,
                out var backLabel) ||
            !_flow.Strings.TryGetValue(
                source.Navigation.Next.LabelRole,
                out var nextLabel) ||
            backLabel != source.Navigation.Back.Label ||
            nextLabel != source.Navigation.Next.Label)
            throw new InvalidOperationException(
                "Owned RaceSexMenu labels or tile contract are incomplete.");
        if (appearanceMenu.RenderedDevice is not { } renderedDevice)
            throw new InvalidOperationException(
                "Owned RaceSex rendered-device contract is absent.");
        var modalRoot = OpenModalRoot("appearance");
        _raceSexRenderedDeviceHost = new OpeningRaceSexRenderedDeviceHost(
            renderedDevice,
            modalRoot,
            _flow.ReferenceCanvasSize,
            _configuration,
            _loaded.MainContent.Lighting,
            _loaded.UnitsToMeters);
        var root = _raceSexRenderedDeviceHost.CreateMenuPresentationHost(
            source.SharedControls.BackgroundRect);
        _raceSexMenuHost = new OpeningRaceSexMenuHost(
            source,
            _opening.MainMenuColor,
            _opening.Style,
            root,
            RaceSexSliderPreviousEngineLabel,
            RaceSexSliderNextEngineLabel,
            _raceSexRenderedDeviceHost.SetActiveList);
        _raceSexMenuHost.FaceGrabHost();
        var preview = _raceSexRenderedDeviceHost.CreateFacePresentationHost();
        var previewControls = FaceGenPreviewControls(faceGen);
        var ageControl = faceGen.ControlSpace.NativeAgeControl;
        var previewBindings = previewControls.Append(
            new OpeningNativeFaceGenGeometryControl(
                -1,
                ageControl.SettingEntity,
                ageControl.SourceLabel,
                ageControl.GeometryAxisSha256)).ToArray();
        var textureControls = faceGen.PreviewHead.Previews[0].TextureControls;
        OwnedGamebryoFaceGenPreviewHost? previewHost = null;
        OpeningPlayerFaceGenPreview? selectedPreviewState = null;

        void RefreshPreview() => previewHost?.SetPreviewState(
            _bodyProportions,
            _appearancePreviewFaceFraming,
            greenProjection: _appearancePreviewMode == "2d");

        OwnedGamebryoFaceGenAgeState CurrentAgeState(float rawValue)
        {
            var geometry = FaceSymmetricGeometryCoordinates(
                faceGen, _faceGeometryControlValues);
            var texture = OwnedGamebryoFaceGenTextureRuntime.Coordinates(
                faceGen.SymmetricTextureValues,
                textureControls,
                _faceTextureControlValues,
                previewPolicy.MorphWeightScale);
            return OwnedGamebryoFaceGenAgeRuntime.Evaluate(
                ageControl, geometry, texture, rawValue);
        }

        void ApplyAge()
        {
            if (_faceAgeRawValue is not { } rawValue || previewHost is null)
                return;
            var state = CurrentAgeState(rawValue);
            previewHost.ApplyAge(
                ageControl.SettingEntity,
                state.GeometryAxisCoefficient,
                ageControl.TextureAxis,
                state.TextureAxisCoefficient);
        }

        void UpdateControlValue(
            OpeningNativeFaceGenGeometryControl control,
            float value)
        {
            if (!float.IsFinite(value) ||
                value < previewPolicy.Minimum ||
                value > previewPolicy.Maximum)
                throw new InvalidOperationException(
                    "FaceGen RaceSexMenu UI value is invalid.");
            var uiValue = Mathf.IsEqualApprox(
                value,
                previewPolicy.ResetValue)
                ? previewPolicy.ResetValue
                : value;
            var morphWeight = uiValue * previewPolicy.MorphWeightScale;
            _faceGeometryControlValues[control.SettingEntity] = uiValue;
            OwnedGamebryoFaceGenMorphRuntime.Publish(
                previewHost,
                control.SettingEntity,
                uiValue);
            ApplyAge();
            RefreshPreview();
            GD.Print(
                $"OPENNV_NEW_GAME_FACEGEN_CONTROL name={control.SettingEntity} " +
                $"axisSha256={control.AxisSha256} uiValue={uiValue:R} " +
                $"morphWeight={morphWeight:R} " +
                $"semantics={previewPolicy.Semantics}");
        }

        void UpdateTextureValue(
            OpeningNativeFaceGenTextureControl control,
            float value)
        {
            if (!float.IsFinite(value) ||
                value < previewPolicy.Minimum || value > previewPolicy.Maximum)
                throw new InvalidOperationException(
                    "FaceGen RaceSexMenu tone value is invalid.");
            _faceTextureControlValues[control.SettingEntity] = value;
            previewHost?.ApplyTexture(control.SettingEntity, value);
            ApplyAge();
            RefreshPreview();
        }

        void UpdateAgeValue(float value)
        {
            var state = CurrentAgeState(value);
            _faceAgeRawValue = value;
            ApplyAge();
            RefreshPreview();
            GD.Print(
                $"OPENNV_NEW_GAME_FACEGEN_AGE setting={ageControl.SettingEntity} " +
                $"rawValue={value:R} years={state.Years:R} " +
                $"geometry={state.SymmetricGeometrySha256} " +
                $"texture={state.SymmetricTextureSha256} semantics={ageControl.Semantics}");
        }

        void RenderPreview(OpeningAppearanceSex sex)
        {
            if (previewHost is not null)
            {
                var disposed = previewHost.DisposeOwnedTree();
                GD.Print(
                    "OPENNV_NEW_GAME_FACEGEN_PREVIEW_DISPOSED " +
                    $"control={disposed.ControlInstanceId} " +
                    $"viewport={disposed.ViewportInstanceId} " +
                    $"actor={disposed.ActorInstanceId} " +
                    $"disposition={disposed.Disposition}");
            }
            foreach (var child in preview.GetChildren())
                child.Free();
            previewHost = null;
            _appearancePreviewHost = null;
            var engineSex = appearance.SexEngineValues[_sexIndex];
            var selectedPreview = OwnedGamebryoFaceGenSelectionInventory.Require(
                faceGen.PreviewHead,
                engineSex,
                _raceFormId,
                _hairFormId,
                _eyesFormId);
            selectedPreviewState = selectedPreview;
            previewHost = OwnedGamebryoFaceGenPreviewHost.Load(
                selectedPreview,
                previewBindings,
                previewPolicy,
                preview,
                _configuration,
                _loaded.MainContent.Lighting,
                _loaded.UnitsToMeters,
                source.FaceGrab.Rect.Size,
                renderedDevice.FaceGenPreviewDevice);
            foreach (var control in previewControls)
                OwnedGamebryoFaceGenMorphRuntime.Publish(
                    previewHost,
                    control.SettingEntity,
                    _faceGeometryControlValues[control.SettingEntity]);
            foreach (var control in selectedPreview.TextureControls)
                previewHost.ApplyTexture(
                    control.SettingEntity,
                    _faceTextureControlValues[control.SettingEntity]);
            ApplyAge();
            RefreshPreview();
            _appearancePreviewHost = previewHost;
            GD.Print(
                $"OPENNV_NEW_GAME_FACEGEN_PREVIEW_READY " +
                $"player={selectedPreview.PlayerFormId} " +
                $"race={selectedPreview.RaceFormId} " +
                $"sex={selectedPreview.Sex} " +
                $"hair={selectedPreview.HairFormId} " +
                $"eyes={selectedPreview.EyesFormId} " +
                $"boundControls={previewHost.BoundControlCount} " +
                $"boundSurfaces={previewHost.BoundSurfaceCount} " +
                $"availableControls={selectedPreview.GeometryControlCount}");
        }

        OpeningAppearanceRace CurrentRace() =>
            appearance.Races.Single(value => value.FormId.Equals(
                _raceFormId,
                StringComparison.OrdinalIgnoreCase));

        OpeningAppearanceSex CurrentSex() =>
            CurrentRace().Sex[appearance.SexEngineValues[_sexIndex]];

        void Accept()
        {
            if (selectedPreviewState is not { FullBody: true } selectedPreview ||
                previewHost is null ||
                selectedPreview.BodyComponentRoles is not { Count: > 0 } bodyRoles)
                throw new InvalidOperationException(
                    "Owned RaceSexMenu full-body preview state is incomplete at accept.");
            GD.Print(
                $"OPENNV_NEW_GAME_APPEARANCE_CONFIRMED " +
                $"sex={appearance.SexEngineValues[_sexIndex]} " +
                $"race={_raceFormId} hair={_hairFormId} eyes={_eyesFormId} " +
                $"faceGeometry={faceGen.SymmetricGeometrySha256} " +
                $"editedFaceGeometry={CurrentFaceSymmetricGeometrySha256()} " +
                $"controls={FaceGenControlValuesText(previewControls)} " +
                $"previewStatus={selectedPreview.Status} " +
                $"previewRuntime={selectedPreview.RuntimeDisposition} " +
                $"fullBody={selectedPreview.FullBody} " +
                $"previewMode={_appearancePreviewMode} " +
                $"bodyProportions={_bodyProportions} " +
                $"bodyRoles={string.Join(',', bodyRoles)} " +
                $"boundFaceGenSurfaces={previewHost.BoundSurfaceCount} " +
                $"boundFaceGenControls={previewHost.BoundControlCount}");
            CloseModal();
            completed();
        }

        Action showSex = null!;
        Action showRace = null!;
        Action showHair = null!;
        Action showEyes = null!;
        Action showFace = null!;
        Action showBody = null!;

        showSex = () => _raceSexMenuHost!.ShowList(
            "sex",
            [
                new OpeningRaceSexListEntry(
                    "sex-header",
                    _flow.Character.SexTitle,
                    false,
                    false,
                    () => { }),
                .. _flow.Character.SexChoices.Select((label, index) =>
                {
                    var choiceIndex = index;
                    return new OpeningRaceSexListEntry(
                        appearance.SexEngineValues[index],
                        label,
                        _sexIndex == index,
                        true,
                        () =>
                        {
                            _sexIndex = choiceIndex;
                            ResolveCurrentAppearanceForSex(resetToRaceDefaults: false);
                            RenderPreview(CurrentSex());
                            showSex();
                        });
                }),
            ],
            null,
            showRace);
        showRace = () => _raceSexMenuHost!.ShowList(
            "race",
            appearance.Races.Select(race =>
            {
                var selectedRace = race;
                return new OpeningRaceSexListEntry(
                    race.FormId,
                    race.Label,
                    race.FormId.Equals(_raceFormId, StringComparison.OrdinalIgnoreCase),
                    true,
                    () =>
                    {
                        _raceFormId = selectedRace.FormId;
                        var sex = CurrentSex();
                        _hairFormId = sex.DefaultHairFormId;
                        _eyesFormId = sex.DefaultEyesFormId;
                        RenderPreview(sex);
                        showRace();
                    });
            }).ToArray(),
            showSex,
            showHair);
        showHair = () =>
        {
            var sex = CurrentSex();
            _raceSexMenuHost!.ShowList(
                "hair",
                sex.HairOptions.Select(option =>
                {
                    var selectedOption = option;
                    return new OpeningRaceSexListEntry(
                        option.FormId,
                        option.Label,
                        option.FormId.Equals(
                            _hairFormId,
                            StringComparison.OrdinalIgnoreCase),
                        true,
                        () =>
                        {
                            _hairFormId = selectedOption.FormId;
                            RenderPreview(CurrentSex());
                            showHair();
                        });
                }).ToArray(),
                showRace,
                showEyes);
        };
        showEyes = () =>
        {
            var sex = CurrentSex();
            _raceSexMenuHost!.ShowList(
                "eyes",
                sex.EyeOptions.Select(option =>
                {
                    var selectedOption = option;
                    return new OpeningRaceSexListEntry(
                        option.FormId,
                        option.Label,
                        option.FormId.Equals(
                            _eyesFormId,
                            StringComparison.OrdinalIgnoreCase),
                        true,
                        () =>
                        {
                            _eyesFormId = selectedOption.FormId;
                            RenderPreview(CurrentSex());
                            showEyes();
                        });
                }).ToArray(),
                showHair,
                showFace);
        };
        showFace = () =>
        {
            _appearancePreviewFaceFraming = true;
            RefreshPreview();
            _raceSexMenuHost!.ShowSliders(
                "faceGeometry",
                previewControls.Select(control =>
                    new OpeningRaceSexSliderEntry(
                        control.SettingEntity,
                        control.SourceLabel,
                        _faceGeometryControlValues[control.SettingEntity],
                        previewPolicy.Minimum,
                        previewPolicy.Maximum,
                        previewPolicy.Step,
                        previewPolicy.Jump,
                        value => value.ToString(
                            "+0;-0;0",
                            System.Globalization.CultureInfo.InvariantCulture),
                        value =>
                        {
                            UpdateControlValue(control, value);
                            showFace();
                        }))
                .Concat(textureControls.Select(control =>
                    new OpeningRaceSexSliderEntry(
                        control.SettingEntity,
                        control.SourceLabel,
                        _faceTextureControlValues[control.SettingEntity],
                        previewPolicy.Minimum,
                        previewPolicy.Maximum,
                        previewPolicy.Step,
                        previewPolicy.Jump,
                        value => value.ToString(
                            "+0;-0;0",
                            System.Globalization.CultureInfo.InvariantCulture),
                        value =>
                        {
                            UpdateTextureValue(control, value);
                            showFace();
                        })))
                .Append(new OpeningRaceSexSliderEntry(
                    ageControl.SourceLabel,
                    ageControl.SettingEntity,
                    _faceAgeRawValue ?? OwnedGamebryoFaceGenAgeRuntime.InitialRawValue(
                        ageControl,
                        FaceSymmetricGeometryCoordinates(
                            faceGen, _faceGeometryControlValues)),
                    ageControl.RawMinimum,
                    ageControl.RawMaximum,
                    ageControl.RawStep,
                    ageControl.RawStep,
                    value => CurrentAgeState(value).Years.ToString(
                        "0",
                        System.Globalization.CultureInfo.InvariantCulture),
                    value =>
                    {
                        UpdateAgeValue(value);
                        showFace();
                    }))
                .ToArray(),
                showEyes,
                Accept);
        };
        showBody = () =>
        {
            _appearancePreviewFaceFraming = false;
            RefreshPreview();
            _raceSexMenuHost!.ShowSliders(
                "body",
                new[]
                {
                    (Key: "height", Label: "Height"),
                    (Key: "chest", Label: "Chest"),
                    (Key: "shoulders", Label: "Shoulders"),
                    (Key: "waist", Label: "Waist"),
                    (Key: "arms", Label: "Arms"),
                    (Key: "thighs", Label: "Thighs"),
                    (Key: "calves", Label: "Calves"),
                }.Select(row =>
                {
                    var selected = row;
                    return new OpeningRaceSexSliderEntry(
                        selected.Key,
                        selected.Label,
                        _bodyProportions.Value(selected.Key),
                        CharacterBodyProportions.Minimum,
                        CharacterBodyProportions.Maximum,
                        CharacterBodyProportions.Step,
                        CharacterBodyProportions.Jump,
                        value => $"{Mathf.RoundToInt(value * 100.0f)}%",
                        value =>
                        {
                            _bodyProportions = _bodyProportions.With(selected.Key, value);
                            RefreshPreview();
                            showBody();
                        });
                }).ToArray(),
                showFace,
                Accept);
        };
        _raceSexShowSex = showSex;
        _raceSexShowFace = showFace;
        _raceSexShowBody = showBody;
        _raceSexRenderedDeviceHost.ConfigureCharacterControls(
            source.Font,
            showSex,
            showRace,
            () =>
            {
                _appearancePreviewFaceFraming = true;
                RefreshPreview();
                showFace();
                _raceSexRenderedDeviceHost.SetCreatorModeState(
                    "FACE",
                    bodyEnabled: false,
                    projectionEnabled: _appearancePreviewMode == "2d",
                    faceEnabled: true);
            },
            showHair,
            () =>
            {
                _appearancePreviewFaceFraming = true;
                RefreshPreview();
                showFace();
                _raceSexRenderedDeviceHost.SetCreatorModeState(
                    "FACE",
                    bodyEnabled: false,
                    projectionEnabled: _appearancePreviewMode == "2d",
                    faceEnabled: true);
            },
            () =>
            {
                _appearancePreviewFaceFraming = false;
                RefreshPreview();
                showBody();
                _raceSexRenderedDeviceHost.SetCreatorModeState(
                    "BODY",
                    bodyEnabled: true,
                    projectionEnabled: _appearancePreviewMode == "2d",
                    faceEnabled: false);
            },
            () =>
            {
                _appearancePreviewMode = _appearancePreviewMode == "3d"
                    ? "2d"
                    : "3d";
                RefreshPreview();
                _raceSexRenderedDeviceHost.SetCreatorModeState(
                    _appearancePreviewFaceFraming ? "FACE" : "BODY",
                    !_appearancePreviewFaceFraming,
                    projectionEnabled: _appearancePreviewMode == "2d",
                    faceEnabled: _appearancePreviewFaceFraming);
            });
        RenderPreview(CurrentSex());
        _raceSexRenderedDeviceHost.SetCreatorModeState(
            "FACE",
            bodyEnabled: false,
            projectionEnabled: _appearancePreviewMode == "2d",
            faceEnabled: true);
        showSex();
        GD.Print(
            "OPENNV_NEW_GAME_APPEARANCE_LAYOUT_STATIC_PASS " +
            $"canvas={_flow.ReferenceCanvasSize} faceGrab={source.FaceGrab.Rect} " +
            $"facePresentation={_raceSexRenderedDeviceHost.FacePresentationRect} " +
            $"menuPresentation={_raceSexRenderedDeviceHost.MenuPresentationRect} " +
            $"sourcePanel={source.Background.Rect} " +
            $"listRow={source.ListItem.Rect} sliderRow={source.Slider.Rect} " +
            $"scrollUp={source.Scroll.Up.Rect} scrollDown={source.Scroll.Down.Rect} " +
            "activeList=sex oneActiveList=True " +
            "authority=hashed-owned-racesex-menu-tiles-v1");
    }

    private void ResolveCurrentAppearanceForSex(bool resetToRaceDefaults)
    {
        var appearance = _flow.Character.Appearance;
        var engineSex = appearance.SexEngineValues[_sexIndex];
        var race = appearance.Races.SingleOrDefault(value => value.FormId.Equals(
            _raceFormId,
            StringComparison.OrdinalIgnoreCase)) ?? appearance.Races.Single(value =>
            value.FormId.Equals(
                appearance.DefaultRaceFormId,
                StringComparison.OrdinalIgnoreCase));
        _raceFormId = race.FormId;
        var sex = race.Sex[engineSex];
        if (resetToRaceDefaults || !sex.HairOptions.Any(value => value.FormId.Equals(
                _hairFormId,
                StringComparison.OrdinalIgnoreCase)))
            _hairFormId = sex.DefaultHairFormId;
        if (resetToRaceDefaults || !sex.EyeOptions.Any(value => value.FormId.Equals(
                _eyesFormId,
                StringComparison.OrdinalIgnoreCase)))
            _eyesFormId = sex.DefaultEyesFormId;
    }

    private void ShowSpecialMenu(Action completed)
    {
        var content = OpenPanel(MenuRect("special", "tagSkills"));
        var title = NewLabel(_flow.SceneRoles["vigorTester"].DisplayName);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);
        foreach (var value in _flow.Character.SpecialValues)
        {
            var row = new HBoxContainer();
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            content.AddChild(row);
            var label = NewLabel(value.Name);
            label.TooltipText = value.Description;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);
            var decrease = NewButton("−");
            decrease.Disabled = _specialValues[value.FormId] <= _flow.Character.SpecialMinimum;
            decrease.Pressed += () =>
            {
                _specialValues[value.FormId]--;
                ShowSpecialMenu(completed);
            };
            row.AddChild(decrease);
            var current = NewLabel(_specialValues[value.FormId].ToString());
            current.HorizontalAlignment = HorizontalAlignment.Center;
            row.AddChild(current);
            var increase = NewButton("+");
            increase.Disabled =
                _specialValues[value.FormId] >= _flow.Character.SpecialMaximum ||
                _specialValues.Values.Sum() >= _flow.Character.SpecialTotalPoints;
            increase.Pressed += () =>
            {
                _specialValues[value.FormId]++;
                ShowSpecialMenu(completed);
            };
            row.AddChild(increase);
        }
        var remaining = _flow.Character.SpecialTotalPoints - _specialValues.Values.Sum();
        var counter = NewLabel(remaining.ToString());
        counter.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(counter);
        var footer = new HBoxContainer();
        footer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(footer);
        var reset = NewButton(_flow.Strings["reset"]);
        reset.Pressed += () =>
        {
            foreach (var value in _flow.Character.SpecialValues)
                _specialValues[value.FormId] = _flow.Character.SpecialInitial;
            ShowSpecialMenu(completed);
        };
        footer.AddChild(reset);
        var accept = NewButton(_flow.Strings["accept"]);
        accept.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        accept.Disabled = remaining != 0;
        accept.Pressed += () =>
        {
            _docReaction = CalculateDocReaction();
            CloseModal();
            completed();
        };
        footer.AddChild(accept);
    }

    private int CalculateDocReaction()
    {
        var rule = _flow.Character.DocReaction;
        OpeningDocReactionValue? extreme = null;
        var maximumDeviation = float.MinValue;
        var low = false;
        foreach (var value in rule.Values)
        {
            var current = _specialValues[value.FormId];
            var deviation = MathF.Abs(current - rule.AverageValue);
            if (deviation <= maximumDeviation)
                continue;
            maximumDeviation = deviation;
            low = current < rule.AverageValue;
            extreme = value;
        }
        if (extreme is null ||
            maximumDeviation < (low
                ? rule.LowDeviationThreshold
                : rule.HighDeviationThreshold))
            return rule.DefaultReaction;
        return low ? extreme.LowReaction : extreme.HighReaction;
    }

    private void ShowSkillMenu(Action completed)
    {
        if (!_skillDefaultsInitialized)
        {
            foreach (var value in _flow.Character.SkillValues
                .OrderByDescending(value => _psychologyScores.GetValueOrDefault(value.SourceName))
                .Take(_flow.Character.TagSkillMaximumSelected))
                _tagSkills.Add(value.FormId);
            _skillDefaultsInitialized = true;
        }
        ShowSelectionMenu(
            "tagSkills",
            _flow.Character.SkillValues,
            _tagSkills,
            _flow.Character.TagSkillMaximumSelected,
            true,
            completed,
            () => _tagSkills.Clear());
    }

    private void ShowTraitMenu(Action completed) => ShowSelectionMenu(
        "traits",
        _flow.Character.TraitValues,
        _traits,
        _flow.Character.TraitMaximumSelected,
        false,
        completed,
        () => _traits.Clear());

    private void ShowSelectionMenu(
        string role,
        IReadOnlyList<OpeningCharacterValue> values,
        HashSet<string> selected,
        int maximum,
        bool requireMaximum,
        Action completed,
        Action resetSelection)
    {
        var content = OpenPanel(MenuRect(role));
        var title = NewLabel(_flow.Menus[role].MenuName);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(title);
        var columns = new HBoxContainer();
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(columns);
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(scroll);
        var list = new VBoxContainer();
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(list);
        var details = new VBoxContainer();
        details.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(details);
        var selectedValue = values.FirstOrDefault(value => selected.Contains(value.FormId)) ??
            values.First();
        AddCharacterDetails(details, selectedValue);
        foreach (var value in values)
        {
            var button = NewButton(value.Name);
            button.ToggleMode = true;
            button.ButtonPressed = selected.Contains(value.FormId);
            button.TooltipText = value.Description;
            button.Disabled = !button.ButtonPressed && selected.Count >= maximum;
            button.Pressed += () =>
            {
                if (!selected.Remove(value.FormId) && selected.Count < maximum)
                    selected.Add(value.FormId);
                ShowSelectionMenu(
                    role,
                    values,
                    selected,
                    maximum,
                    requireMaximum,
                    completed,
                    resetSelection);
            };
            button.MouseEntered += () =>
            {
                foreach (var child in details.GetChildren())
                    child.QueueFree();
                AddCharacterDetails(details, value);
            };
            list.AddChild(button);
        }
        var count = NewLabel($"{selected.Count}/{maximum}");
        count.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(count);
        var footer = new HBoxContainer();
        footer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.AddChild(footer);
        var reset = NewButton(_flow.Strings["reset"]);
        reset.Pressed += () =>
        {
            resetSelection();
            ShowSelectionMenu(
                role,
                values,
                selected,
                maximum,
                requireMaximum,
                completed,
                resetSelection);
        };
        footer.AddChild(reset);
        var accept = NewButton(_flow.Strings["accept"]);
        accept.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        accept.Disabled = requireMaximum && selected.Count != maximum;
        accept.Pressed += () =>
        {
            CloseModal();
            completed();
        };
        footer.AddChild(accept);
    }

    private void AddCharacterDetails(Node parent, OpeningCharacterValue value)
    {
        if (value.IconPath is not null)
        {
            parent.AddChild(new TextureRect
            {
                Texture = OwnedUiTheme.LoadTexture(value.IconPath),
                ExpandMode = TextureRect.ExpandModeEnum.KeepSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        var name = NewLabel(value.Name);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        parent.AddChild(name);
        var description = NewLabel(value.Description);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        parent.AddChild(description);
    }
}
