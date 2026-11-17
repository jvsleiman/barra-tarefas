using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>
/// 0.4.0 personalization pages, built in code: Aparência › Cores (with a live preview) and Botão Iniciar, and the
/// Barra page (open apps, quick settings, folders, AI usage). Every change saves at once, so the dock and the
/// preview update live.
/// </summary>
public partial class SettingsWindow
{
    private const string PersonalTag = "personal";
    private static readonly string[] Swatches = ["#3B82F6", "#22C55E", "#F59E0B", "#EF4444", "#A855F7", "#EC4899", "#14B8A6", "#FFFFFF"];
    private static readonly string[] BarSwatches = ["#0A0A0C", "#111827", "#1E1B2E", "#0F2027", "#1F2937", "#202020", "#F3F4F6"];

    private DockSettings Current => _preferences.Current;

    private void SavePersonal(Func<DockSettings, DockSettings> change)
    {
        var error = _preferences.Save(change(Current));
        if (error is not null) MessageBox.Show(this, error, "OpenTaskbar", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RenderPersonalization()
    {
        RenderAppearanceExtras();
        RenderBarPage();
    }

    // ---- Aparência › Cores + Botão Iniciar ----

    private void RenderAppearanceExtras()
    {
        AppearanceExtras.Children.Clear();
        AppearanceExtras.Children.Add(Section("SectionColors", "ColorsDescription"));
        AppearanceExtras.Children.Add(Preview());
        AppearanceExtras.Children.Add(ColorRow("ColorAccent", Current.AccentColor, Swatches, value => Current with { AccentColor = value }));
        AppearanceExtras.Children.Add(ColorRow("ColorBar", Current.BarColor, BarSwatches, value => Current with { BarColor = value }));
        AppearanceExtras.Children.Add(ColorRow("ColorTile", Current.TileColor, Swatches, value => Current with { TileColor = value }));
        AppearanceExtras.Children.Add(SliderRow("TileOpacity", 0, 50, Current.TileOpacity * 100, "{0:0}%", value => Current with { TileOpacity = Math.Round(value) / 100 }));
        AppearanceExtras.Children.Add(SliderRow("CornerRadius", 0, 24, Current.CornerRadius, "{0:0}", value => Current with { CornerRadius = Math.Round(value) }));
        AppearanceExtras.Children.Add(ColorRow("ColorWeekday", Current.WeekdayColor, Swatches, value => Current with { WeekdayColor = value }));
        AppearanceExtras.Children.Add(ColorRow("ColorRunningDot", Current.RunningDotColor, Swatches, value => Current with { RunningDotColor = value }));
        var reset = PersonalButton("ColorsReset", () => SavePersonal(settings => settings with
        {
            AccentColor = "", BarColor = "", TileColor = "", TileOpacity = 0.055, CornerRadius = 15, WeekdayColor = "", RunningDotColor = ""
        }), refresh: true);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 12, 0, 0);
        AppearanceExtras.Children.Add(reset);

        AppearanceExtras.Children.Add(Section("StartButton", null));
        AppearanceExtras.Children.Add(ChoiceRow("StartIconStyle",
        [
            ("StartIconGrey", Current.StartIcon == StartIconStyle.GreyLogo, () => SavePersonal(s => s with { StartIcon = StartIconStyle.GreyLogo })),
            ("StartIconWindows", Current.StartIcon == StartIconStyle.WindowsLogo, () => SavePersonal(s => s with { StartIcon = StartIconStyle.WindowsLogo })),
            ("StartIconImage", Current.StartIcon == StartIconStyle.CustomImage, PickStartImage),
            ("StartIconSystem", Current.StartIcon == StartIconStyle.SystemIcon, PickSystemStartIcon),
            ("StartIconText", Current.StartIcon == StartIconStyle.Text, () => SavePersonal(s => s with { StartIcon = StartIconStyle.Text }))
        ]));
        AppearanceExtras.Children.Add(SliderRow("StartIconSize", 60, 160, Current.StartIconScale * 100, "{0:0}%", value => Current with { StartIconScale = Math.Round(value) / 100 }));
    }

    /// <summary>A miniature dock drawn with the live resources: bar, tiles, running dot and weekday color.</summary>
    private FrameworkElement Preview()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var index = 0; index < 4; index++)
        {
            var tile = new Border { Width = 34, Height = 34, Margin = new Thickness(3), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            tile.SetResourceReference(Border.BackgroundProperty, "DockTile");
            tile.SetResourceReference(Border.BorderBrushProperty, "DockTileStroke");
            var glyph = new Rectangle { Width = 14, Height = 14, RadiusX = 3, RadiusY = 3, Opacity = 0.75 };
            glyph.SetResourceReference(Shape.FillProperty, index == 0 ? "WindowsLogoBrush" : "TextSecondary");
            var dot = new Rectangle { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2), RadiusX = 1.5, RadiusY = 1.5 };
            dot.SetResourceReference(WidthProperty, "RunningIndicatorWidth");
            dot.SetResourceReference(Shape.FillProperty, "RunningDotBrush");
            dot.Visibility = index is 1 or 2 ? Visibility.Visible : Visibility.Hidden;
            tile.Child = new Grid { Children = { glyph, dot } };
            row.Children.Add(tile);
        }

        var weekday = new TextBlock { Text = UiText.CultureFor(Current.Language).DateTimeFormat.GetAbbreviatedDayName(DateTime.Today.DayOfWeek).TrimEnd('.').ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.Bold };
        weekday.SetResourceReference(TextBlock.ForegroundProperty, "WeekdayAccent");
        var day = new TextBlock { Text = DateTime.Today.Day.ToString(), FontSize = 18, FontWeight = FontWeights.Bold };
        day.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        var accent = new Border { Width = 3, Height = 22, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(10, 0, 0, 0) };
        accent.SetResourceReference(Border.BackgroundProperty, "WidgetAccent");
        var card = new Border { Margin = new Thickness(6, 3, 3, 3), Padding = new Thickness(10, 2, 12, 2), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { weekday, day } }, accent } } };
        card.SetResourceReference(Border.BackgroundProperty, "DockTile");
        card.SetResourceReference(Border.BorderBrushProperty, "DockTileStroke");
        row.Children.Add(card);

        var bar = new Border { Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = row };
        bar.SetResourceReference(Border.BackgroundProperty, "DockMaterial");
        bar.SetResourceReference(Border.BorderBrushProperty, "DockBorderMaterial");
        bar.CornerRadius = new CornerRadius(Current.CornerRadius);
        var label = new TextBlock { Text = UiText.Get("PreviewLabel"), Style = (Style)FindResource("SettingsRowDescription"), Margin = new Thickness(0, 0, 0, 6) };
        // A fixed backdrop keeps the translucent bar readable in the preview.
        return new StackPanel { Margin = new Thickness(0, 4, 0, 10), Children = { label, new Border { Background = new SolidColorBrush(Color.FromRgb(0x5B, 0x6B, 0x8C)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Left, Child = bar } } };
    }

    private void PickSystemStartIcon()
    {
        if (AppDialogs.PickStartIcon() is { } chosen)
            SavePersonal(settings => settings with { StartIcon = chosen.Style, StartIconPath = chosen.Path });
        RenderAppearanceExtras();
    }

    private void PickStartImage()
    {
        var picker = new OpenFileDialog { Title = UiText.Get("StartIconPick"), Filter = "Imagens (*.png;*.ico;*.jpg;*.jpeg;*.bmp)|*.png;*.ico;*.jpg;*.jpeg;*.bmp" };
        if (picker.ShowDialog(this) == true && picker.FileName.Length <= 400)
            SavePersonal(settings => settings with { StartIcon = StartIconStyle.CustomImage, StartIconPath = picker.FileName });
        RenderAppearanceExtras();
    }

    // ---- Barra page ----

    private void RenderBarPage()
    {
        BarPage.Children.Clear();
        BarPage.Children.Add(new TextBlock { Text = UiText.Get("BarPage"), Style = (Style)FindResource("SettingsHeading") });

        BarPage.Children.Add(Section("SectionRunningApps", null));
        BarPage.Children.Add(SwitchRow("ShowAppTitles", "ShowAppTitlesDescription", Current.ShowAppTitles, on => Current with { ShowAppTitles = on }));
        BarPage.Children.Add(ChoiceRow("IndicatorStyle",
        [
            ("IndicatorDot", Current.IndicatorStyle == RunningIndicatorStyle.Dot, () => SavePersonal(s => s with { IndicatorStyle = RunningIndicatorStyle.Dot })),
            ("IndicatorBar", Current.IndicatorStyle == RunningIndicatorStyle.Bar, () => SavePersonal(s => s with { IndicatorStyle = RunningIndicatorStyle.Bar }))
        ]));
        BarPage.Children.Add(SwitchRow("ThisMonitorApps", "ThisMonitorAppsDescription", Current.ThisMonitorAppsOnly, on => Current with { ThisMonitorAppsOnly = on }));

        // Order of the cards on the bar (the same order drag-and-drop on the bar changes); the switch shows/hides each one.
        BarPage.Children.Add(Section("SectionBlockOrder", "BlockOrderDescription"));
        var blockOrder = (Current.WidgetOrder is { Count: > 0 } saved ? saved : OpenTaskbar.Widgets.WidgetCoordinator.DefaultWidgetOrder)
            .Concat(OpenTaskbar.Widgets.WidgetCoordinator.DefaultWidgetOrder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var enabled = Current.Widgets ?? [];
        var shown = blockOrder.Where(id => enabled.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (var id in shown)
        {
            var position = shown.IndexOf(id);
            BarPage.Children.Add(ListRow(UiText.Get(OpenTaskbar.Widgets.WidgetCoordinator.GetLabelKey(id) ?? id), true,
                on => SetWidgetEnabled(id, on),
                position > 0 ? () => SetBlockOrder(blockOrder, id, shown[position - 1]) : null,
                position < shown.Count - 1 ? () => SetBlockOrder(blockOrder, id, shown[position + 1]) : null));
        }
        BarPage.Children.Add(Section("SectionQuickSettings", "QuickSettingsItemsDescription"));
        var chosen = Current.EffectiveQuickSettingsItems.ToList();
        var order = chosen.Concat(DockSettings.AllQuickSettingsItems.Where(item => !chosen.Contains(item))).ToList();
        foreach (var item in order)
        {
            var isOn = chosen.Contains(item);
            BarPage.Children.Add(ListRow(QuickSettingsLabel(item), isOn,
                on => SetQuickSettings(on ? chosen.Append(item).ToList() : chosen.Where(id => id != item).ToList()),
                isOn && chosen.IndexOf(item) > 0 ? () => SetQuickSettings(Move(chosen, item, -1)) : null,
                isOn && chosen.IndexOf(item) < chosen.Count - 1 ? () => SetQuickSettings(Move(chosen, item, 1)) : null));
        }

        BarPage.Children.Add(SwitchRow("QuickSettingsCompact", "QuickSettingsCompactDescription", Current.QuickSettingsCompact, on => Current with { QuickSettingsCompact = on }));

        BarPage.Children.Add(Section("SectionFolders", "FoldersDescription"));
        var groups = Current.AppGroups ?? [];
        if (groups.Count == 0)
            BarPage.Children.Add(new TextBlock { Text = UiText.Get("FolderNone"), Style = (Style)FindResource("SettingsRowDescription") });
        foreach (var group in groups) BarPage.Children.Add(FolderRow(group));

        BarPage.Children.Add(Section("SectionAiUsage", "AiUsageProvidersDescription"));
        var providers = Current.AiUsageProviders ?? AiUsage.DefaultProviders;
        foreach (var provider in AiUsage.Providers)
        {
            BarPage.Children.Add(ListRow(provider switch { AiUsage.Codex => "Codex (ChatGPT)", AiUsage.Claude => "Claude Code", _ => provider + " · " + UiText.Get("NoUsageData") }, providers.Contains(provider), on =>
            {
                var next = AiUsage.Providers.Where(p => p == provider ? on : providers.Contains(p)).ToList();
                SavePersonal(settings => settings with { AiUsageProviders = next });
                RenderBarPage();
            }, null, null));
        }
    }

    /// <summary>Swaps two cards in the saved order (neighbours among the visible ones may be far apart in the full list).</summary>
    private void SetBlockOrder(IReadOnlyList<string> order, string id, string other)
    {
        var list = order.ToList();
        var a = list.IndexOf(id);
        var b = list.IndexOf(other);
        if (a < 0 || b < 0) return;
        (list[a], list[b]) = (list[b], list[a]);
        SavePersonal(settings => settings with { WidgetOrder = list });
        RenderBarPage();
    }

    private void SetWidgetEnabled(string id, bool on)
    {
        SavePersonal(settings => settings with
        {
            Widgets = on ? (settings.Widgets ?? []).Append(id).Distinct().ToList() : (settings.Widgets ?? []).Where(w => w != id).ToList(),
            InstalledWidgets = (settings.InstalledWidgets ?? []).Append(id).Distinct().ToList()
        });
        RenderBarPage();
    }

    private void SetQuickSettings(IReadOnlyList<string> items)
    {
        SavePersonal(settings => settings with { QuickSettingsItems = items });
        RenderBarPage();
    }

    /// <summary>Moves an item one step up (-1) or down (+1).</summary>
    public static List<string> Move(IReadOnlyList<string> items, string item, int step)
    {
        var list = items.ToList();
        var index = list.IndexOf(item);
        var target = index + step;
        if (index < 0 || target < 0 || target >= list.Count) return list;
        (list[index], list[target]) = (list[target], list[index]);
        return list;
    }

    private static string QuickSettingsLabel(string item) => UiText.Get(item switch
    {
        "Wifi" => "WiFi",
        "Panel" => "QuickSettingsPanel",
        "Notifications" => "NotificationCenter",
        _ => item
    });

    private FrameworkElement FolderRow(DockAppGroup group)
    {
        var name = new TextBlock { Text = $"{group.Name}  ·  {group.Apps.Count}", Style = (Style)FindResource("SettingsRowLabel") };
        var colors = SwatchPanel(group.Color ?? "", Swatches, value =>
        {
            SavePersonal(settings => settings with
            {
                AppGroups = (settings.AppGroups ?? []).Select(existing => existing.Name == group.Name ? existing with { Color = value } : existing).ToList()
            });
            RenderBarPage();
        });
        var remove = PersonalButton("AppGroupUngroup", () =>
        {
            SavePersonal(settings => settings with { AppGroups = (settings.AppGroups ?? []).Where(existing => existing.Name != group.Name).ToList() is { Count: > 0 } rest ? rest : null });
            RenderBarPage();
        });
        remove.Margin = new Thickness(12, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(remove, Dock.Right);
        DockPanel.SetDock(colors, Dock.Right);
        row.Children.Add(remove);
        row.Children.Add(colors);
        row.Children.Add(name);
        return new Border { Style = (Style)FindResource("SettingsRow"), Child = row };
    }

    // ---- Row builders ----

    private TextBlock Section(string titleKey, string? descriptionKey)
    {
        var title = new TextBlock { Text = UiText.Get(titleKey), Style = (Style)FindResource("SettingsSection") };
        if (descriptionKey is null) return title;
        title.Inlines.Add(new System.Windows.Documents.LineBreak());
        title.Inlines.Add(new System.Windows.Documents.Run(UiText.Get(descriptionKey)) { FontSize = 12, FontWeight = FontWeights.Normal });
        return title;
    }

    private Border SwitchRow(string labelKey, string descriptionKey, bool value, Func<bool, DockSettings> change)
    {
        var toggle = new CheckBox { IsChecked = value, Tag = PersonalTag, VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("SettingsSwitch") };
        System.Windows.Automation.AutomationProperties.SetName(toggle, UiText.Get(labelKey));
        toggle.Click += (_, _) => SavePersonal(_ => change(toggle.IsChecked == true));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = UiText.Get(labelKey), Style = (Style)FindResource("SettingsRowLabel") });
        text.Children.Add(new TextBlock { Text = UiText.Get(descriptionKey), Style = (Style)FindResource("SettingsRowDescription") });
        var row = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        row.Children.Add(text);
        return new Border { Style = (Style)FindResource("SettingsRow"), Child = row };
    }

    private Border ListRow(string label, bool value, Action<bool> toggled, Action? up, Action? down)
    {
        var toggle = new CheckBox { IsChecked = value, Tag = PersonalTag, VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("SettingsSwitch") };
        System.Windows.Automation.AutomationProperties.SetName(toggle, label);
        toggle.Click += (_, _) => toggled(toggle.IsChecked == true);
        var row = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        foreach (var (glyph, key, action) in new[] { ("", "MoveUp", up), ("", "MoveDown", down) })
        {
            if (action is null) continue;
            var arrow = new Button { Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), Width = 32, Height = 28, Margin = new Thickness(0, 0, 6, 0), ToolTip = UiText.Get(key) };
            arrow.SetResourceReference(StyleProperty, "WidgetIconButton");
            System.Windows.Automation.AutomationProperties.SetName(arrow, $"{label}: {UiText.Get(key)}");
            arrow.Click += (_, _) => action();
            DockPanel.SetDock(arrow, Dock.Right);
            row.Children.Add(arrow);
        }

        row.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("SettingsRowLabel") });
        return new Border { Style = (Style)FindResource("CompactSettingsRow"), Child = row };
    }

    private StackPanel ChoiceRow(string labelKey, IReadOnlyList<(string Key, bool Selected, Action Choose)> options)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(new TextBlock { Text = UiText.Get(labelKey), Style = (Style)FindResource("SettingsChoiceLabel") });
        var grid = new UniformGrid { Columns = options.Count, Rows = 1 };
        var group = $"personal-{labelKey}";
        foreach (var (key, selected, choose) in options)
        {
            var radio = new RadioButton { Content = UiText.Get(key), IsChecked = selected, GroupName = group, Tag = PersonalTag, Style = (Style)FindResource("SettingsChoice") };
            radio.Click += (_, _) => choose();
            grid.Children.Add(radio);
        }

        panel.Children.Add(grid);
        return panel;
    }

    private StackPanel SliderRow(string labelKey, double min, double max, double value, string format, Func<double, DockSettings> change)
    {
        var readout = new TextBlock { Text = string.Format(format, value), Foreground = (Brush)FindResource("TextSecondary"), HorizontalAlignment = HorizontalAlignment.Right };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Tag = PersonalTag, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 6, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(slider, UiText.Get(labelKey));
        slider.ValueChanged += (_, args) => readout.Text = string.Format(format, args.NewValue);
        // Save when the drag ends (or on keyboard/click steps) so the settings file isn't rewritten on every pixel.
        slider.PreviewMouseUp += (_, _) => SavePersonal(_ => change(slider.Value));
        slider.KeyUp += (_, _) => SavePersonal(_ => change(slider.Value));
        var header = new DockPanel();
        DockPanel.SetDock(readout, Dock.Right);
        header.Children.Add(readout);
        header.Children.Add(new TextBlock { Text = UiText.Get(labelKey), FontWeight = FontWeights.SemiBold });
        return new StackPanel { Margin = new Thickness(0, 10, 0, 6), Children = { header, slider } };
    }

    private Border ColorRow(string labelKey, string value, IReadOnlyList<string> swatches, Func<string, DockSettings> change)
    {
        var swatchPanel = SwatchPanel(value, swatches, chosen =>
        {
            SavePersonal(_ => change(chosen));
            RenderAppearanceExtras();
        });
        var row = new DockPanel();
        DockPanel.SetDock(swatchPanel, Dock.Right);
        row.Children.Add(swatchPanel);
        row.Children.Add(new TextBlock { Text = UiText.Get(labelKey), Style = (Style)FindResource("SettingsRowLabel") });
        return new Border { Style = (Style)FindResource("SettingsRow"), Child = row };
    }

    /// <summary>"Padrão" + preset swatches + "Personalizada…" (a #RRGGBB prompt); the current choice is outlined.</summary>
    private StackPanel SwatchPanel(string value, IReadOnlyList<string> swatches, Action<string> choose)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var reset = PersonalButton("ColorDefault", () => choose(""));
        reset.Padding = new Thickness(10, 4, 10, 4);
        reset.Margin = new Thickness(0, 0, 8, 0);
        reset.FontWeight = string.IsNullOrEmpty(value) ? FontWeights.Bold : FontWeights.Normal;
        panel.Children.Add(reset);
        foreach (var hex in swatches)
        {
            var color = ThemeManager.ParseColor(hex)!.Value;
            var selected = string.Equals(hex, value, StringComparison.OrdinalIgnoreCase);
            var swatch = new Button
            {
                Width = 22, Height = 22, Margin = new Thickness(2, 0, 2, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = hex,
                Template = SwatchTemplate(new SolidColorBrush(color), selected)
            };
            System.Windows.Automation.AutomationProperties.SetName(swatch, hex);
            swatch.Click += (_, _) => choose(hex);
            panel.Children.Add(swatch);
        }

        var custom = PersonalButton("ColorCustom", () =>
        {
            var typed = TextPrompt.Ask(UiText.Get("ColorCustom"), "#RRGGBB", string.IsNullOrEmpty(value) ? "#" : value)?.Trim();
            if (typed is null) return;
            if (!typed.StartsWith('#')) typed = "#" + typed;
            if (DockSettingsStore.IsColorOrEmpty(typed) && typed.Length == 7) choose(typed.ToUpperInvariant());
        });
        custom.Padding = new Thickness(10, 4, 10, 4);
        custom.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(custom);
        return panel;
    }

    private static ControlTemplate SwatchTemplate(Brush fill, bool selected)
    {
        var ellipse = new FrameworkElementFactory(typeof(Ellipse));
        ellipse.SetValue(Shape.FillProperty, fill);
        ellipse.SetValue(Shape.StrokeThicknessProperty, selected ? 2.5 : 1.0);
        ellipse.SetResourceReference(Shape.StrokeProperty, selected ? "StrokeFocus" : "StrokeSubtle");
        return new ControlTemplate(typeof(Button)) { VisualTree = ellipse };
    }

    private Button PersonalButton(string key, Action click, bool refresh = false)
    {
        var button = new Button { Content = UiText.Get(key), Style = (Style)FindResource("SettingsButton"), Tag = PersonalTag, Padding = new Thickness(14, 6, 14, 6) };
        button.Click += (_, _) =>
        {
            click();
            if (refresh) RenderPersonalization();
        };
        return button;
    }
}
