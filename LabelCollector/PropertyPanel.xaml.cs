using System.Windows;
using System.Windows.Controls;
using LabelCollector.ViewModels;

namespace LabelCollector;

/// <summary>右側のプロパティウィンドウ。中間データ（.lc）の中身や、シートの設定を編集する</summary>
public partial class PropertyPanel : UserControl
{
    public PropertyPanel()
    {
        InitializeComponent();
    }

    // 名前・フォルダ・キー列名などの入力が終わったら、その項目が入っている中間データに保存
    private void Setting_LostFocus(object sender, RoutedEventArgs e)
    {
        switch ((sender as FrameworkElement)?.DataContext)
        {
            case SheetViewModel sheet:
                sheet.RequestSave();
                break;
            case ExcelSourceViewModel source:
                source.RequestSave();
                break;
        }
    }
}
