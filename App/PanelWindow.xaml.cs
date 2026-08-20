using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using verba_windows.Models;
using verba_windows.Utilities;
using verba_windows.ViewModels;

namespace verba_windows.AppHost;

public partial class PanelWindow : Window
{
    private bool _initializing = true;
    private CustomTone? _editingTone;

    public PanelWindow(TranslationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = ViewModel = viewModel;
        ApplyTheme();
        viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(viewModel.IsTranslating)) UpdateToneSaveState(); };
        _initializing = false;
    }

    public TranslationViewModel ViewModel { get; }
    public event EventHandler? HideRequested;
    public event EventHandler? QuitRequested;
    public event EventHandler<ShortcutEventArgs>? ShortcutChangeRequested;

    public void SetShortcutState(HotkeyGesture shortcut, bool registered, bool rejected = false)
    {
        ShortcutBox.Text = shortcut.DisplayText;
        ShortcutStatus.Text = rejected ? ViewModel.Strings.ShortcutInUse
            : registered ? "" : ViewModel.Strings.ShortcutUnavailable;
    }

    public async void FocusInitial(bool externalSelection = false)
    {
        await Task.Delay(60);
        if (!IsVisible) return;
        if (externalSelection || !ViewModel.IsEmptyState) FreeformBox.Focus(); else SourceBox.Focus();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new nint(style | NativeMethods.WsExToolWindow));
    }

    private void ApplyTheme()
    {
        var light = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (key?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        catch { }
        Resources["PanelBrush"] = Brush(light ? "#FFFDFD" : "#202124");
        Resources["FooterBrush"] = Brush(light ? "#F7F7F8" : "#191A1C");
        Resources["TextBrush"] = Brush(light ? "#1C1C1E" : "#F2F2F2");
        Resources["MutedBrush"] = Brush("#8C8C8C"); Resources["AccentBrush"] = Brush("#4F6BED");
        Resources["AccentSoftBrush"] = Brush(light ? "#294F6BED" : "#3D6F86FF");
        Resources["PillBrush"] = Brush(light ? "#0F000000" : "#16FFFFFF");
        Resources["ChipBorderBrush"] = Brush(light ? "#26000000" : "#36FFFFFF");
        Resources["HairlineBrush"] = Brush(light ? "#14000000" : "#22FFFFFF");
        Resources["PanelBorderBrush"] = Brush(light ? "#1A000000" : "#40FFFFFF");
    }

    private static SolidColorBrush Brush(string value) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ShortcutBox.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Escape)
            {
                Keyboard.ClearFocus();
                ShortcutStatus.Text = "";
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (ToneEditor.Visibility == Visibility.Visible) CloseToneEditor(); else HideRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true; return;
        }
        if (e.Key == Key.Return && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key != Key.ImeProcessed)
        { if (ViewModel.CanCopy) CopyAndClose(); e.Handled = true; }
    }

    private void Editor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed || e.ImeProcessedKey != Key.None) return;
        if (e.Key != Key.Return || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "source": ViewModel.TranslateNow(); break;
            case "freeform": ViewModel.ApplyFreeform(); break;
            case "tone": SaveTone(); break;
        }
        e.Handled = true;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    { if (!GearPopup.IsOpen) HideRequested?.Invoke(this, EventArgs.Empty); }

    private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null && current != this)
        {
            if (current is System.Windows.Controls.Button or System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox or System.Windows.Controls.ComboBoxItem) return;
            current = VisualTreeHelper.GetParent(current);
        }
        try { DragMove(); } catch { }
    }

    private void Auto_Click(object s, RoutedEventArgs e) => ViewModel.SetAutoDetectSource(!ViewModel.IsAutoDetectSource);
    private void Swap_Click(object s, RoutedEventArgs e) { ViewModel.SwapLanguages(); SourceLanguageBox.SelectedItem = ViewModel.SourceLanguage; TargetLanguageBox.SelectedItem = ViewModel.TargetLanguage; }
    private void SourceLanguage_SelectionChanged(object s, SelectionChangedEventArgs e) { if (!_initializing && SourceLanguageBox.SelectedItem is TranslationLanguage x) ViewModel.SetSourceLanguage(x); }
    private void TargetLanguage_SelectionChanged(object s, SelectionChangedEventArgs e) { if (!_initializing && TargetLanguageBox.SelectedItem is TranslationLanguage x) ViewModel.SetTargetLanguage(x); }
    private void Clear_Click(object s, RoutedEventArgs e) { ViewModel.ClearAll(); SourceBox.Focus(); }
    private void SourceSpeech_Click(object s, RoutedEventArgs e) => ViewModel.ToggleSourceSpeech();
    private void ResultSpeech_Click(object s, RoutedEventArgs e) => ViewModel.ToggleResultSpeech();
    private void Casual_Click(object s, RoutedEventArgs e) => ViewModel.ToggleTone(Tone.Casual);
    private void Neutral_Click(object s, RoutedEventArgs e) => ViewModel.ToggleTone(Tone.Neutral);
    private void Formal_Click(object s, RoutedEventArgs e) => ViewModel.ToggleTone(Tone.Formal);
    private void Shorter_Click(object s, RoutedEventArgs e) => ViewModel.ToggleAction(RefineAction.Shorter);
    private void Natural_Click(object s, RoutedEventArgs e) => ViewModel.ToggleAction(RefineAction.Natural);
    private void KeepTerms_Click(object s, RoutedEventArgs e) => ViewModel.ToggleAction(RefineAction.KeepTerms);
    private void Explain_Click(object s, RoutedEventArgs e) => ViewModel.ToggleAction(RefineAction.Explain);
    private void Freeform_Click(object s, RoutedEventArgs e) => ViewModel.ApplyFreeform();
    private void Undo_Click(object s, RoutedEventArgs e) => ViewModel.Undo();
    private void Redo_Click(object s, RoutedEventArgs e) => ViewModel.Redo();
    private void Copy_Click(object s, RoutedEventArgs e) => CopyAndClose();
    private void Gear_Click(object s, RoutedEventArgs e)
    {
        var active = ViewModel.LanguageStore.Current;
        EnglishLanguageButton.Content = (active == AppLanguage.En ? "✓ " : "  ") + "English";
        VietnameseLanguageButton.Content = (active == AppLanguage.Vi ? "✓ " : "  ") + "Tiếng Việt";
        KoreanLanguageButton.Content = (active == AppLanguage.Ko ? "✓ " : "  ") + "한국어";
        GearPopup.IsOpen = true;
    }
    private void GearPopup_Closed(object? s, EventArgs e) { if (!IsActive) HideRequested?.Invoke(this, EventArgs.Empty); }
    private void Quit_Click(object s, RoutedEventArgs e) => QuitRequested?.Invoke(this, EventArgs.Empty);

    private void Shortcut_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ShortcutBox.SelectAll();
        ShortcutStatus.Text = "";
    }

    private void Shortcut_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };
        var shortcut = new HotkeyGesture(Keyboard.Modifiers, key);
        if (!shortcut.IsValid)
        {
            ShortcutStatus.Text = ViewModel.Strings.ShortcutNeedsModifier;
            e.Handled = true;
            return;
        }
        ShortcutChangeRequested?.Invoke(this, new ShortcutEventArgs(shortcut));
        e.Handled = true;
    }

    private void ResetShortcut_Click(object sender, RoutedEventArgs e) =>
        ShortcutChangeRequested?.Invoke(this, new ShortcutEventArgs(HotkeyGesture.Default));

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id) ViewModel.LanguageStore.Current = AppLanguageExtensions.Parse(id);
        GearPopup.IsOpen = false;
    }

    private void CustomTone_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.DataContext is CustomTone tone) ViewModel.ToggleCustomTone(tone); }
    private void CustomTone_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((sender as FrameworkElement)?.ContextMenu?.Items is not { Count: >= 2 } items) return;
        if (items[0] is MenuItem edit) edit.Header = ViewModel.Strings.EditCustomTone;
        if (items[1] is MenuItem delete) delete.Header = ViewModel.Strings.DeleteCustomTone;
    }

    private static CustomTone? ToneFromMenu(object sender)
    {
        if (sender is not MenuItem item || item.Parent is not ContextMenu menu) return null;
        return (menu.PlacementTarget as FrameworkElement)?.DataContext as CustomTone;
    }

    private void EditTone_Click(object sender, RoutedEventArgs e) { var tone = ToneFromMenu(sender); if (tone is not null) OpenToneEditor(tone); }
    private void DeleteTone_Click(object sender, RoutedEventArgs e) { var tone = ToneFromMenu(sender); if (tone is not null) ViewModel.DeleteTone(tone); }
    private void AddTone_Click(object s, RoutedEventArgs e) => OpenToneEditor(null);
    private void CancelTone_Click(object s, RoutedEventArgs e) => CloseToneEditor();
    private void SaveTone_Click(object s, RoutedEventArgs e) => SaveTone();
    private void ToneDraft_TextChanged(object sender, TextChangedEventArgs e) => UpdateToneSaveState();
    private void UpdateToneSaveState() => SaveToneButton.IsEnabled = !ViewModel.IsTranslating && ToneDraftBox.Text.Trim().Length > 0;

    private async void OpenToneEditor(CustomTone? tone)
    {
        _editingTone = tone; ToneDraftBox.Text = tone?.Instruction ?? ""; ToneEditor.Visibility = Visibility.Visible;
        await Task.Delay(60); ToneDraftBox.Focus(); ToneDraftBox.CaretIndex = ToneDraftBox.Text.Length;
    }
    private void CloseToneEditor()
    {
        _editingTone = null; ToneDraftBox.Text = ""; ToneEditor.Visibility = Visibility.Collapsed;
        if (ViewModel.IsEmptyState) SourceBox.Focus(); else FreeformBox.Focus();
    }
    private void SaveTone()
    {
        var text = ToneDraftBox.Text.Trim();
        if (ViewModel.IsTranslating || text.Length == 0) return;
        ViewModel.SaveTone(_editingTone, text); CloseToneEditor();
    }

    private async void CopyAndClose()
    {
        if (!ViewModel.CanCopy) return;
        try { System.Windows.Clipboard.SetText(ViewModel.TranslatedText); } catch { return; }
        CopyButton.Content = ViewModel.Strings.Copied; HideRequested?.Invoke(this, EventArgs.Empty);
        await Task.Delay(1200); CopyButton.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("Strings.CopyAndClose"));
    }
}
