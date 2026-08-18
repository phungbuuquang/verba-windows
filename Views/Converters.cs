using System.Globalization;
using System.Windows;
using System.Windows.Data;
using verba_windows.Models;

namespace verba_windows.Views;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class LanguageNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [TranslationLanguage language, AppLanguage appLanguage, ..] ? language.Name(appLanguage) : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => targetTypes.Select(_ => System.Windows.Data.Binding.DoNothing).ToArray();
}

public sealed class CustomToneSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [CustomTone custom, ToneSelection.Custom selected, ..] && custom.Id == selected.Tone.Id;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => targetTypes.Select(_ => System.Windows.Data.Binding.DoNothing).ToArray();
}

public sealed class EmptyStringVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = string.IsNullOrEmpty(value as string);
        if (parameter as string == "invert") empty = !empty;
        return empty ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
