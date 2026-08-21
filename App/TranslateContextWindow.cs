using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using verba_windows.Services;
using verba_windows.Utilities;

namespace verba_windows.AppHost;

internal sealed class TranslateContextWindow : Window, IDisposable
{
    private static readonly nint HwndTopmost = new(-1);
    private readonly AppLanguageStore _language;
    private readonly TextBlock _label;
    private readonly Border _surface;
    private System.Drawing.Rectangle _physicalBounds;

    public TranslateContextWindow(AppLanguageStore language)
    {
        _language = language;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        _label = new TextBlock
        {
            Text = language.Strings.TranslateWithVerba,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };
        var icon = new System.Windows.Controls.Image
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            Source = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/AppIcon.png"))
        };
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(_label);
        _surface = new Border
        {
            Child = row,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(11, 8, 13, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.24
            }
        };
        Content = new Border { Padding = new Thickness(8), Child = _surface };
        ApplyTheme();

        SourceInitialized += OnSourceInitialized;
        _surface.MouseEnter += (_, _) => _surface.Opacity = 0.82;
        _surface.MouseLeave += (_, _) => _surface.Opacity = 1;
        _surface.PreviewMouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Invoked?.Invoke(this, EventArgs.Empty);
        };
        language.PropertyChanged += OnLanguageChanged;
    }

    public event EventHandler? Invoked;

    public bool Contains(System.Drawing.Point point) =>
        IsVisible && _physicalBounds.Contains(point);

    public void ShowAt(System.Drawing.Point point, nint targetWindow)
    {
        Opacity = 0;
        if (!IsVisible) Show();
        UpdateLayout();

        var handle = new WindowInteropHelper(this).Handle;
        var dpi = targetWindow == 0 ? 96u : NativeMethods.GetDpiForWindow(targetWindow);
        if (dpi == 0) dpi = 96;
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi / 96d));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi / 96d));
        var screen = System.Windows.Forms.Screen.FromPoint(point).WorkingArea;
        var x = Math.Clamp(point.X + 10, screen.Left + 4, Math.Max(screen.Left + 4, screen.Right - width - 4));
        var preferredY = point.Y - height - 8;
        var y = preferredY >= screen.Top + 4
            ? preferredY
            : Math.Min(screen.Bottom - height - 4, point.Y + 14);

        _physicalBounds = new System.Drawing.Rectangle(x, y, width, height);
        NativeMethods.SetWindowPos(handle, HwndTopmost, x, y, width, height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        Opacity = 1;
    }

    public new void Hide()
    {
        _physicalBounds = System.Drawing.Rectangle.Empty;
        base.Hide();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
            new nint(style | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppLanguageStore.Strings))
            _label.Text = _language.Strings.TranslateWithVerba;
    }

    private void ApplyTheme()
    {
        var light = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (key?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        catch { }
        _surface.Background = Brush(light ? "#FFFDFD" : "#292A2D");
        _surface.BorderBrush = Brush(light ? "#26000000" : "#42FFFFFF");
        _label.Foreground = Brush(light ? "#1C1C1E" : "#F2F2F2");
    }

    private static SolidColorBrush Brush(string value) => new(
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));

    public void Dispose()
    {
        _language.PropertyChanged -= OnLanguageChanged;
        Close();
    }
}
