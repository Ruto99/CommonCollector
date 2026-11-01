using System.Windows;
using System.Windows.Controls;
using LabelCollector.ViewModels;

namespace LabelCollector;

/// <summary>右側のプロパティウィンドウ。選択中のシートの設定を編集する</summary>
public partial class PropertyPanel : UserControl
{
    public PropertyPanel()
    {
        InitializeComponent();
    }

    // 名前・フォルダ・キー列名などの入力が終わったら保存
    private void Setting_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.SelectedSheet?.RequestSave();
    }
}
