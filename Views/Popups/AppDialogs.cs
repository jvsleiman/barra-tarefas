using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using OpenTaskbar.Services;

namespace OpenTaskbar;

/// <summary>Small modal dialogs styled like the dock menus (same frame as <see cref="TextPrompt"/>).</summary>
public static class AppDialogs
{
    // ---- Suggestions ----

    /// <summary>Asks for a suggestion and appends it to sugestoes.md. Returns true when one was saved.</summary>
    public static bool AddSuggestion(SuggestionStore store, Action<string> reportError)
    {
        var window = CreateWindow(UiText.Get("SuggestionAdd"), 440);
        var text = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 12),
            Padding = new Thickness(8, 6, 8, 6), FontSize = 13
        };
        StyleInput(text);
        var category = new ComboBox { ItemsSource = SuggestionStore.Categories, SelectedIndex = 0, MinWidth = 140 };
        var categoryRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        categoryRow.Children.Add(new TextBlock { Text = UiText.Get("SuggestionCategory"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        categoryRow.Children.Add(category);

        var saved = false;
        var save = DialogButton(UiText.Get("SuggestionSave"), isDefault: true);
        save.IsDefault = false; // Enter makes new lines in the text box; Ctrl+Enter saves.
        save.Click += (_, _) =>
        {
            var error = store.Add(text.Text, category.SelectedItem as string ?? SuggestionStore.Categories[0],
                UpdateChecker.CurrentVersion.ToString(3), DateTime.Now);
            if (error is not null)
            {
                reportError(error);
                return;
            }

            saved = true;
            window.DialogResult = true;
        };
        text.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                args.Handled = true;
                save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
        };

        var body = new StackPanel();
        body.Children.Add(Heading(UiText.Get("SuggestionPrompt")));
        body.Children.Add(text);
        body.Children.Add(categoryRow);
        body.Children.Add(Buttons(DialogButton(UiText.Get("Cancel"), isCancel: true), save));
        Show(window, body, () => text.Focus());
        return saved;
    }

    // ---- Updates ----

    /// <summary>
    /// Checks GitHub and offers the right next step: update now, "you're up to date", or — when the repository
    /// can't be read anonymously (private) — open the download page / install from a downloaded file.
    /// </summary>
    public static void CheckForUpdate(Func<UpdateChecker.Release, Task> startUpdate)
    {
        var window = CreateWindow(UiText.Get("UpdateDialogTitle"), 420);
        var message = new TextBlock { Text = UiText.Get("UpdateChecking"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = DialogButton(UiText.Get("Close"), isCancel: true);
        buttons.Children.Add(close);
        var body = new StackPanel();
        body.Children.Add(Heading(UiText.Get("UpdateDialogTitle")));
        body.Children.Add(message);
        body.Children.Add(buttons);
        var check = new CancellationTokenSource();
        window.Closed += (_, _) => check.Cancel();
        window.Loaded += async (_, _) =>
        {
            var result = await UpdateChecker.CheckAsync(check.Token);
            if (check.IsCancellationRequested) return;
            switch (result)
            {
                case { Status: UpdateChecker.UpdateStatus.Available, Release: { } release }:
                    message.Text = UiText.Format("UpdateAvailableVersion", release.Tag.TrimStart('v', 'V'));
                    var update = DialogButton(UiText.Get("UpdateNow"), isDefault: true);
                    update.Click += async (_, _) =>
                    {
                        window.DialogResult = true;
                        await startUpdate(release);
                    };
                    buttons.Children.Add(update);
                    break;
                case { Status: UpdateChecker.UpdateStatus.UpToDate }:
                    message.Text = UiText.Format("UpdateLatest", UpdateChecker.CurrentVersion.ToString(3));
                    break;
                default:
                    message.Text = UiText.Get("UpdateCannotCheck");
                    var open = DialogButton(UiText.Get("UpdateOpenDownloads"));
                    open.Click += (_, _) =>
                    {
                        ShellShortcuts.OpenUri(UpdateChecker.ReleasesPage);
                        window.DialogResult = true;
                    };
                    var fromFile = DialogButton(UiText.Get("UpdateInstallFromFile"));
                    fromFile.Click += (_, _) => InstallFromFile(window, message);
                    // Full-width rows: the Portuguese labels are too long to share one line with "Fechar".
                    var choices = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                    foreach (var choice in new[] { open, fromFile })
                    {
                        choice.Margin = new Thickness(0, 0, 0, 6);
                        choice.HorizontalAlignment = HorizontalAlignment.Stretch;
                        choice.HorizontalContentAlignment = HorizontalAlignment.Left;
                        choices.Children.Add(choice);
                    }

                    body.Children.Insert(body.Children.IndexOf(buttons), choices);
                    break;
            }
        };
        Show(window, body, () => close.Focus());
    }

    private static void InstallFromFile(Window owner, TextBlock message)
    {
        var picker = new OpenFileDialog
        {
            Title = UiText.Get("UpdatePickFile"),
            Filter = "OpenTaskbar (*.exe)|*.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
                ? System.IO.Path.Combine(home, "Downloads") : null
        };
        if (picker.ShowDialog(owner) != true) return;
        // On success the app exits and the updater script restarts the new version.
        if (UpdateChecker.ApplyFromFile(picker.FileName) is { } error) message.Text = error;
    }

    // ---- Start icon gallery ----

    /// <summary>
    /// Gallery of Windows' own icons (imageres.dll / shell32.dll) plus the account picture. Returns the chosen style and
    /// its path ("imageres.dll,15" or an image file), or null when cancelled.
    /// </summary>
    public static (Models.StartIconStyle Style, string Path)? PickStartIcon()
    {
        var window = CreateWindow(UiText.Get("StartIconGallery"), 560);
        (Models.StartIconStyle, string)? result = null;
        var grid = new WrapPanel();
        var scroll = new ScrollViewer { Height = 340, Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 12) };
        var library = new ComboBox { ItemsSource = SystemIconLibrary.Libraries, SelectedIndex = 0, MinWidth = 160 };
        void Fill()
        {
            grid.Children.Clear();
            var file = (string)library.SelectedItem;
            var count = Math.Min(SystemIconLibrary.Count(file), 400);
            for (var index = 0; index < count; index++)
            {
                var spec = $"{file},{index}";
                if (SystemIconLibrary.Load(spec) is not { } image) continue;
                var tile = new Button { Width = 48, Height = 48, Margin = new Thickness(2), ToolTip = spec, Content = new Image { Source = image, Width = 32, Height = 32 } };
                tile.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutButton");
                tile.Click += (_, _) =>
                {
                    result = (Models.StartIconStyle.SystemIcon, spec);
                    window.DialogResult = true;
                };
                grid.Children.Add(tile);
            }
        }

        library.SelectionChanged += (_, _) => Fill();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(library);
        if (SystemIconLibrary.AccountPicture() is { } picture)
        {
            var account = DialogButton(UiText.Get("StartIconAccount"));
            account.Click += (_, _) =>
            {
                result = (Models.StartIconStyle.CustomImage, picture);
                window.DialogResult = true;
            };
            top.Children.Add(account);
        }

        var body = new StackPanel();
        body.Children.Add(Heading(UiText.Get("StartIconGallery")));
        body.Children.Add(top);
        body.Children.Add(scroll);
        body.Children.Add(Buttons(DialogButton(UiText.Get("Cancel"), isCancel: true)));
        window.Loaded += (_, _) => Fill();
        Show(window, body, () => library.Focus());
        return result;
    }

    // ---- Shared frame ----

    private static Window CreateWindow(string title, double width)
    {
        var window = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false, Topmost = true
        };
        window.SetResourceReference(Window.FontFamilyProperty, "InterfaceFont");
        return window;
    }

    private static void Show(Window window, UIElement body, Action focus)
    {
        var frame = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(18), Child = body };
        frame.SetResourceReference(Border.BackgroundProperty, "MenuMaterial");
        frame.SetResourceReference(Border.BorderBrushProperty, "MenuStroke");
        frame.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        frame.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed && args.OriginalSource is not TextBox) window.DragMove();
        };
        window.Content = frame;
        window.SourceInitialized += (_, _) => MainWindow.ExcludeFromTaskSwitcher(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        window.Loaded += (_, _) => focus();
        window.ShowDialog();
    }

    private static TextBlock Heading(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, FontSize = 14 };

    private static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in buttons) panel.Children.Add(button);
        return panel;
    }

    private static Button DialogButton(string content, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = content, IsDefault = isDefault, IsCancel = isCancel, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "FlyoutButton");
        return button;
    }

    private static void StyleInput(Control input)
    {
        input.SetResourceReference(Control.BackgroundProperty, "SettingsControl");
        input.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        input.SetResourceReference(Control.BorderBrushProperty, "MenuStroke");
        if (input is TextBox box) box.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimary");
    }
}
