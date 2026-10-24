using System.Windows;
using System.Windows.Controls;
using LabelCollector.ViewModels;

namespace LabelCollector;

/// <summary>
/// AvalonDock のタブ（シート）にだけスタイルを当てる。
/// XAML で作った左ペイン・ログ欄には当てない。
/// </summary>
public class SheetItemStyleSelector : StyleSelector
{
    public Style? DocumentStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container) =>
        item is SheetViewModel ? DocumentStyle : null;
}
