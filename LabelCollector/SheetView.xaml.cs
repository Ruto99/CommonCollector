using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LabelCollector.Models;
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
        if (_sheet != null) _sheet.TabsChanged -= BuildTabs;
        _sheet = sheet;
        if (_sheet != null) _sheet.TabsChanged += BuildTabs;
        BuildTabs();
    }

    /// <summary>
    /// タブを作る：親のタブはデータ元ごと。エクセルフォルダなら、その中に直下・サブフォルダごとの子のタブを並べる。
    /// 選んでいたタブは、作り直しても同じものを選ぶ
    /// </summary>
    private void BuildTabs()
    {
        TabsHost.Content = null;
        if (_sheet == null) return;
        TabsHost.Content = CreateTabControl(_sheet, _sheet.Tabs, parentId: "");
    }

    private TabControl CreateTabControl(SheetViewModel sheet, IReadOnlyList<SheetTab> tabs, string parentId)
    {
        var control = new TabControl { Padding = new Thickness(2) };
        foreach (var tab in tabs)
        {
            var rows = new ListCollectionView(sheet.Rows) { Filter = o => o is RowItem r && tab.Filter(r) };
            var item = new TabItem
            {
                Header = CreateHeader(tab.Header, rows),
                ToolTip = tab.ToolTip,
                Tag = tab.Id,
                Content = tab.Children.Count > 0
                    ? CreateTabControl(sheet, tab.Children, tab.Id)
                    : CreateGrid(tab.Columns, rows),
            };
            control.Items.Add(item);
        }

        // 前に選んでいたタブを選ぶ（無ければ先頭）
        var selected = sheet.SelectedTabs.TryGetValue(parentId, out var id)
            ? control.Items.OfType<TabItem>().FirstOrDefault(t => (string)t.Tag == id)
            : null;
        control.SelectedItem = selected ?? control.Items.OfType<TabItem>().FirstOrDefault();
        control.SelectionChanged += (_, e) =>
        {
            // 子のタブの SelectionChanged が親まで上がってくるので、自分のものだけ見る
            if (!ReferenceEquals(e.OriginalSource, control)) return;
            if (control.SelectedItem is TabItem t) sheet.SelectedTabs[parentId] = (string)t.Tag;
        };
        return control;
    }

    /// <summary>タブの見出し：名前と行数</summary>
    private static StackPanel CreateHeader(string text, ListCollectionView rows)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = text });
        var count = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(6, 0, 0, 0) };
        count.SetBinding(TextBlock.TextProperty, new Binding(nameof(rows.Count)) { Source = rows, StringFormat = "({0})" });
        panel.Children.Add(count);
        return panel;
    }

    /// <summary>
    /// 表を作る：状態・キー → データ元から取り込む列（読み取り専用）→ 手入力の列（編集できる）→ 取得元・最終確認
    /// </summary>
    private DataGrid CreateGrid(IReadOnlyList<SheetColumn> columns, ListCollectionView rows)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            RowStyle = (Style)FindResource("RowStyle"),
        };
        grid.CellEditEnding += DataGrid_CellEditEnding;

        grid.Columns.Add(TextColumn("状態", "State", readOnly: true, width: 70));
        grid.Columns.Add(TextColumn("キー", "Key", readOnly: true, star: 1.5));

        foreach (var column in columns)
        {
            // 列名は "Auto[品名]" のように行の値へバインドする
            var path = (column.IsManual ? "Manual" : "Auto") + "[" + EscapeIndexer(column.Name) + "]";
            var header = column.IsManual ? column.Name + "（手入力）" : column.Name;
            grid.Columns.Add(TextColumn(header, path, readOnly: !column.IsManual, star: column.IsManual ? 2 : 1.5));
        }

        grid.Columns.Add(TextColumn("取得元", "Origin", readOnly: true, star: 1.5));
        var lastSeen = TextColumn("最終確認", "LastSeen", readOnly: true, width: 130);
        ((Binding)lastSeen.Binding).StringFormat = "yyyy/MM/dd HH:mm";
        grid.Columns.Add(lastSeen);
        return grid;
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
