using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTaskbar.Models;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>
/// App groups: pinned apps gathered into one tile (2x2 icon preview) that opens a flyout with the apps.
/// Groups are created and edited from the app / group right-click menus and stored in DockSettings.AppGroups.
/// </summary>
public partial class MainWindow
{
    private readonly ObservableCollection<DockAppGroupView> _appGroups = [];
    private readonly HashSet<string> _groupedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly DockFlyout _groupFlyout = new();
    private string? _appliedGroupSignature;

    private bool IsGrouped(DockApp app) => _groupedPaths.Contains(app.ExecutablePath);

    private IReadOnlyList<DockAppGroup> ConfiguredGroups => _preferences.Current.AppGroups ?? [];

    /// <summary>Builds group tiles from settings; members that are no longer pinned are skipped.</summary>
    private void RebuildAppGroups()
    {
        AppGroupItems.ItemsSource ??= _appGroups;
        var pinnedByPath = _pinnedApps
            .GroupBy(app => app.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var resolved = ConfiguredGroups
            .Select(group => (group.Name, Apps: group.Apps
                .Select(path => pinnedByPath.GetValueOrDefault(path))
                .OfType<DockApp>()
                .Where(app => !IsHiddenByProfile(app))
                .ToList()))
            .Where(group => group.Apps.Count > 0)
            .ToList();
        var signature = string.Join(";", resolved.Select(group => group.Name + "=" + string.Join("|", group.Apps.Select(app => app.ExecutablePath))));
        if (signature == _appliedGroupSignature) return;
        _appliedGroupSignature = signature;

        foreach (var group in _appGroups) group.Dispose();
        _appGroups.Clear();
        _groupedPaths.Clear();
        foreach (var (name, apps) in resolved)
        {
            _appGroups.Add(new DockAppGroupView(name, apps));
            foreach (var app in apps) _groupedPaths.Add(app.ExecutablePath);
        }
    }

    private void SaveGroups(IEnumerable<DockAppGroup> groups)
    {
        var list = groups.Where(group => group.Apps.Count > 0).ToList();
        var error = _preferences.Save(_preferences.Current with { AppGroups = list.Count > 0 ? list : null });
        if (error is not null) ShowStatus(error);
    }

    private string UniqueGroupName()
    {
        var baseName = UiText.Get("AppGroup");
        var names = ConfiguredGroups.Select(group => group.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    /// <summary>Puts <paramref name="app"/> into the named group (creating it), removing it from any other group.</summary>
    internal void AddToGroup(DockApp app, string? groupName, DockApp? partner = null)
    {
        var name = groupName ?? UniqueGroupName();
        var groups = ConfiguredGroups
            .Select(group => group with
            {
                Apps = group.Apps.Where(path => !string.Equals(path, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                    && (partner is null || !string.Equals(path, partner.ExecutablePath, StringComparison.OrdinalIgnoreCase))).ToList()
            })
            .ToList();
        var index = groups.FindIndex(group => string.Equals(group.Name, name, StringComparison.CurrentCultureIgnoreCase));
        var members = index >= 0 ? groups[index].Apps.ToList() : [];
        if (partner is not null) members.Add(partner.ExecutablePath);
        members.Add(app.ExecutablePath);
        if (index >= 0) groups[index] = groups[index] with { Apps = members };
        else groups.Add(new DockAppGroup(name, members));
        SaveGroups(groups);
    }

    private void RemoveFromGroup(DockApp app) => SaveGroups(ConfiguredGroups.Select(group => group with
    {
        Apps = group.Apps.Where(path => !string.Equals(path, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToList()
    }));

    /// <summary>Adds "Create group" / "Add to group ›" to a pinned app's right-click menu.</summary>
    private void PopulateGroupMenu(ContextMenu menu, DockApp? app, bool isPinnedTarget)
    {
        foreach (var stale in menu.Items.OfType<FrameworkElement>().Where(item => item.Tag as string == "GroupCommand").ToList())
            menu.Items.Remove(stale);
        if (app is null || !isPinnedTarget) return;
        var insertAt = Math.Max(0, menu.Items.IndexOf(menu.Items.OfType<Separator>().FirstOrDefault(item => item.Tag as string == "RunningCommandsDivider")));
        var create = new MenuItem { Header = UiText.Get("AppGroupCreate"), Icon = FindResource("IconLayers"), Tag = "GroupCommand" };
        create.Click += (_, _) => AddToGroup(app, null);
        menu.Items.Insert(insertAt++, create);
        if (_appGroups.Count == 0) return;
        var addTo = new MenuItem { Header = UiText.Get("AppGroupAddTo"), Icon = FindResource("IconPlus"), Tag = "GroupCommand" };
        foreach (var group in _appGroups)
        {
            var item = new MenuItem { Header = group.Name.Replace("_", "__") };
            item.Click += (_, _) => AddToGroup(app, group.Name);
            addTo.Items.Add(item);
        }
        menu.Items.Insert(insertAt, addTo);
    }

    private void AppGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DockAppGroupView group } button) return;
        _groupFlyout.Toggle(button, () => BuildGroupFlyout(group));
    }

    private UIElement BuildGroupFlyout(DockAppGroupView group)
    {
        var root = new StackPanel { MinWidth = 220 };
        root.Children.Add(DockFlyout.Heading(group.Name));
        var grid = new WrapPanel { MaxWidth = 300 };
        foreach (var app in group.Apps)
        {
            var image = new Image { Source = app.Icon, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var label = new TextBlock
            {
                Text = app.Name, FontSize = 11, MaxWidth = 76, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
            };
            var cell = new StackPanel { Width = 76 };
            cell.Children.Add(image);
            cell.Children.Add(label);
            var button = DockFlyout.FlatButton(cell, (_, _) =>
            {
                _groupFlyout.Close();
                OpenDockApp(app);
            }, app.Name);
            var remove = new MenuItem { Header = UiText.Get("AppGroupRemoveFrom"), Icon = FindResource("IconClose") };
            remove.Click += (_, _) =>
            {
                _groupFlyout.Close();
                RemoveFromGroup(app);
            };
            button.ContextMenu = new ContextMenu { Items = { remove } };
            grid.Children.Add(button);
        }

        root.Children.Add(grid);
        return root;
    }

    private void GroupRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: DockAppGroupView group }) return;
        var name = TextPrompt.Ask(UiText.Get("AppGroupRename"), UiText.Get("AppGroupNamePrompt"))?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == group.Name) return;
        if (ConfiguredGroups.Any(existing => string.Equals(existing.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            return;
        SaveGroups(ConfiguredGroups.Select(existing => existing.Name == group.Name ? existing with { Name = name } : existing));
    }

    private void GroupUngroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: DockAppGroupView group }) return;
        _groupFlyout.Close();
        SaveGroups(ConfiguredGroups.Where(existing => existing.Name != group.Name));
    }
}
