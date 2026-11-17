using System.Windows;
using System.Windows.Controls;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>Settings: "Procurar atualização" button and the Sugestões page (list from sugestoes.md).</summary>
public partial class SettingsWindow
{
    private readonly SuggestionStore _suggestionStore = new();

    private void CheckUpdateNow_Click(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.ShowUpdateDialog();

    private void RenderSuggestions()
    {
        SuggestionsPage.Children.Clear();
        SuggestionsPage.Children.Add(new TextBlock { Text = UiText.Get("Suggestions"), Style = (Style)FindResource("SettingsHeading") });
        SuggestionsPage.Children.Add(new TextBlock { Text = UiText.Get("SuggestionsDescription"), Style = (Style)FindResource("SettingsRowDescription"), Margin = new Thickness(0, 0, 0, 14) });

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        actions.Children.Add(ActionButton("SuggestionAdd", () =>
        {
            if (AppDialogs.AddSuggestion(_suggestionStore, ShowSuggestionError)) RenderSuggestions();
        }));
        actions.Children.Add(ActionButton("SuggestionsOpenFile", () =>
        {
            if (!System.IO.File.Exists(_suggestionStore.FilePath)) ShowSuggestionError(UiText.Get("SuggestionsNone"));
            else ShowSuggestionError(ShellShortcuts.OpenUri(_suggestionStore.FilePath));
        }));
        actions.Children.Add(ActionButton("SuggestionsCopyAll", () =>
        {
            try
            {
                Clipboard.SetText(_suggestionStore.ReadAll());
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ShowSuggestionError(UiText.Get("ErrorClipboardCopy"));
            }
        }));
        SuggestionsPage.Children.Add(actions);

        var items = _suggestionStore.Load();
        if (items.Count == 0)
        {
            SuggestionsPage.Children.Add(new TextBlock { Text = UiText.Get("SuggestionsNone"), Style = (Style)FindResource("SettingsRowDescription") });
            return;
        }

        foreach (var item in items.Reverse())
        {
            var done = new CheckBox
            {
                IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0),
                ToolTip = UiText.Get("SuggestionMarkDone")
            };
            System.Windows.Automation.AutomationProperties.SetName(done, UiText.Get("SuggestionMarkDone"));
            done.Click += (_, _) =>
            {
                ShowSuggestionError(_suggestionStore.Toggle(item.Index));
                RenderSuggestions();
            };
            var delete = ActionButton("SuggestionDelete", () =>
            {
                ShowSuggestionError(_suggestionStore.Delete(item.Index));
                RenderSuggestions();
            });
            delete.Margin = new Thickness(12, 0, 0, 0);
            delete.VerticalAlignment = VerticalAlignment.Top;
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, Opacity = item.Done ? 0.55 : 1,
                TextDecorations = item.Done ? TextDecorations.Strikethrough : null });
            text.Children.Add(new TextBlock { Text = item.Header, Style = (Style)FindResource("SettingsRowDescription") });
            var row = new DockPanel();
            DockPanel.SetDock(done, Dock.Left);
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(done);
            row.Children.Add(delete);
            row.Children.Add(text);
            SuggestionsPage.Children.Add(new Border { Style = (Style)FindResource("SettingsRow"), Child = row });
        }
    }

    private Button ActionButton(string key, Action click)
    {
        var button = new Button { Content = UiText.Get(key), Style = (Style)FindResource("SettingsButton"), Margin = new Thickness(0, 0, 8, 8) };
        button.Click += (_, _) => click();
        return button;
    }

    private void ShowSuggestionError(string? error)
    {
        if (error is not null) MessageBox.Show(this, error, "OpenTaskbar", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
