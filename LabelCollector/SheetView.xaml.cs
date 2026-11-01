using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using LabelCollector.ViewModels;

namespace LabelCollector;

public partial class SheetView : UserControl
{
    private SheetViewModel? _sheet;

    public SheetView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as SheetViewModel);
    }

    private void Attach(SheetViewModel? sheet)
    {
        if (_sheet != null) _sheet.ColumnsChanged -= BuildColumns;
        _sheet = sheet;
        if (_sheet != null) _sheet.ColumnsChanged += BuildColumns;
        BuildColumns();
    }

    /// <summary>
    /// 表の列を作る：状態・キー → データ元から取り込む列（読み取り専用）→ 手入力の列（編集できる）→ 取得元・最終確認
    /// </summary>
    private void BuildColumns()
    {
        Table.Columns.Clear();
        if (_sheet == null) return;

        Table.Columns.Add(TextColumn("状態", "State", readOnly: true, width: 70));
        Table.Columns.Add(TextColumn("キー", "Key", readOnly: true, star: 1.5));

        foreach (var column in _sheet.Columns)
        {
            // 列名は "Auto[品名]" のように行の値へバインドする
            var path = (column.IsManual ? "Manual" : "Auto") + "[" + EscapeIndexer(column.Name) + "]";
            var header = column.IsManual ? column.Name + "（手入力）" : column.Name;
            Table.Columns.Add(TextColumn(header, path, readOnly: !column.IsManual, star: column.IsManual ? 2 : 1.5));
        }

        Table.Columns.Add(TextColumn("取得元", "Origin", readOnly: true, star: 1.5));
        var lastSeen = TextColumn("最終確認", "LastSeen", readOnly: true, width: 130);
        ((Binding)lastSeen.Binding).StringFormat = "yyyy/MM/dd HH:mm";
        Table.Columns.Add(lastSeen);
    }

    private static DataGridTextColumn TextColumn(string header, string path, bool readOnly, double star = 0, double width = 0)
    {
        var binding = new Binding(path) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay };
        if (!readOnly) binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
        return new DataGridTextColumn
        {
            Header = header,
            Binding = binding,
            IsReadOnly = readOnly,
            MinWidth = width > 0 ? width : 80,
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(star, DataGridLengthUnitType.Star),
        };
    }

    /// <summary>バインドのパスの [ ] の中では , [ ] ^ などに ^ を付ける</summary>
    private static string EscapeIndexer(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (c is '^' or ',' or '[' or ']' or '(' or ')' or '.' or '/' or '\\') sb.Append('^');
            sb.Append(c);
        }
        return sb.ToString();
    }

    // 手入力の編集が確定したら自動保存（確定後に値が反映されるので少し遅らせる）
    private void DataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && DataContext is SheetViewModel vm)
            Dispatcher.BeginInvoke(vm.RequestSave, DispatcherPriority.Background);
    }
}
