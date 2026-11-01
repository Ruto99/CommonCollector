using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LabelCollector.Models;
using LabelCollector.Services;

namespace LabelCollector;

/// <summary>データ元の設定 → 「📊 エクセルフォルダ」のような種類の表示名</summary>
public class SourceKindNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SourceSettings s
            ? DataSourceRegistry.Find(s) is { } kind ? $"{kind.Icon} {kind.DisplayName}" : $"（未対応のデータ元: {s.GetType().Name}）"
            : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>件数が 0 のときだけ表示する（「まだありません」などの案内用）</summary>
public class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
