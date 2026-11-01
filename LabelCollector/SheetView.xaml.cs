using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LabelCollector.ViewModels;

namespace LabelCollector;

public partial class SheetView : UserControl
{
    public SheetView()
    {
        InitializeComponent();
    }

    // B列の編集が確定したら自動保存（確定後に値が反映されるので少し遅らせる）
    private void DataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && DataContext is SheetViewModel vm)
            Dispatcher.BeginInvoke(vm.RequestSave, DispatcherPriority.Background);
    }
}
