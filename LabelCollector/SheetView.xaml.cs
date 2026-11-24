using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        MultiValueColumns.Clear();
        PairColumns.Clear();
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
        grid.BeginningEdit += DataGrid_BeginningEdit;
        grid.PreviewKeyDown += DataGrid_PreviewKeyDown;

        grid.Columns.Add(TextColumn("状態", "State", readOnly: true, width: 70));
        grid.Columns.Add(TextColumn("キー", "Key", readOnly: true, star: 1.5));

        // 組の2列（例：部品番号と数量）がシートにそろっていれば、その2列は組で入力する
        var paired = LinkedColumns.IsPresentIn(columns.Where(c => c.IsManual).Select(c => c.Name));

        foreach (var column in columns)
        {
            if (column.IsManual)
            {
                // 手入力の列（どれも複数の値を入れられる）：値が1つまでならセルに直接入力。
                // 2つ目からは、右端の ▾（Alt+↓）でセルの下に開く一覧で入力する。
                // 組の列は、2列いっしょの表（1行に1組）で入力する
                var isPair = paired && LinkedColumns.Contains(column.Name);
                var name = EscapeIndexer(column.Name);
                var listPath = (isPair ? "ManualPair[" : "ManualList[") + name + "]";
                var header = column.Name + (isPair ? "（手入力・組）" : "（手入力）");
                var listColumn = TextColumn(header, listPath, readOnly: false, star: 2);
                // 1文字ごとに書き込むと、前後の空白を取る処理で入力中の空白が消えるので、確定したときに書き込む
                ((Binding)listColumn.Binding).UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
                var listStyle = ManualCellStyle(listPath, toolTip: true, emptyFlagPath: isPair ? "ManualPair.Empty[" + name + "]" : null);
                listStyle.Setters.Add(new Setter(TemplateProperty, FindResource("MultiValueCellTemplate")));
                listColumn.CellStyle = listStyle;
                MultiValueColumns[listColumn] = column.Name;
                if (isPair) PairColumns.Add(listColumn);
                grid.Columns.Add(listColumn);
                continue;
            }

            // データ元から取り込む列は "Auto[品名]" のように行の値へバインドする（読み取り専用）
            var path = "Auto[" + EscapeIndexer(column.Name) + "]";
            grid.Columns.Add(TextColumn(column.Name, path, readOnly: true, star: 1.5));
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

    /// <summary>
    /// 手入力の列のセル：何も入力されていないときは赤い枠で囲む（入力すると消える）。
    /// 枠の太さは DataGridCell の既定（1）のままにして、色だけ変えるので、編集中も大きさは変わらない
    /// </summary>
    /// <param name="toolTip">セルの値をツールチップにも出す（複数の値が幅に収まらないとき用。空なら出さない）</param>
    /// <param name="emptyFlagPath">組の列用：この列の値が空の組があると true になるパス。true なら赤枠にする（もう片方の列だけ入っている組）</param>
    private Style ManualCellStyle(string path, bool toolTip = false, string? emptyFlagPath = null)
    {
        var style = new Style(typeof(DataGridCell), (Style)FindResource(typeof(DataGridCell)));
        var brush = (Brush)FindResource("EmptyManualCellBrush");
        if (toolTip) style.Setters.Add(new Setter(ToolTipProperty, new Binding(path)));
        if (emptyFlagPath != null)
        {
            var missing = new DataTrigger { Binding = new Binding(emptyFlagPath), Value = true };
            missing.Setters.Add(new Setter(Control.BorderBrushProperty, brush));
            style.Triggers.Add(missing);
        }
        foreach (var empty in new object?[] { "", null })
        {
            var trigger = new DataTrigger { Binding = new Binding(path), Value = empty };
            trigger.Setters.Add(new Setter(Control.BorderBrushProperty, brush));
            if (toolTip) trigger.Setters.Add(new Setter(ToolTipProperty, null));
            style.Triggers.Add(trigger);
        }
        return style;
    }

    // ---- 手入力の列：値が1つまではセルで、2つ目からはセルの下の一覧で入力する ----

    /// <summary>表の手入力の列 → 手入力の列名</summary>
    private Dictionary<DataGridColumn, string> MultiValueColumns { get; } = new();

    /// <summary>表の手入力の列のうち、組の列（LinkedColumns）</summary>
    private HashSet<DataGridColumn> PairColumns { get; } = new();

    // 値（組）が2つ以上あるセルは、セルでは編集させずに一覧を開く（どの値を書き換えるのか分からないので）
    private void DataGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (sender is not DataGrid grid || !MultiValueColumns.TryGetValue(e.Column, out var name)) return;
        if (e.Row.Item is not RowItem row) return;
        var count = PairColumns.Contains(e.Column) ? row.ManualPair.Count : row.GetManualValues(name).Count;
        if (count <= 1) return;
        e.Cancel = true;
        if (FindCell(e.Column, row) is { } cell)
            Dispatcher.BeginInvoke(() => OpenValueList(grid, cell, row, name), DispatcherPriority.Input);
    }

    // Alt+↓ で一覧を開く（エクセルのドロップダウンと同じキー）
    private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!(e.Key == Key.System && e.SystemKey == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt)) return;
        if (sender is DataGrid { CurrentCell: { Column: { } column, Item: RowItem row } } grid
            && MultiValueColumns.TryGetValue(column, out var name) && FindCell(column, row) is { } cell)
        {
            OpenValueList(grid, cell, row, name);
            e.Handled = true;
        }
    }

    // セルの右端の ▾
    private void MultiValueButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.TemplatedParent is not DataGridCell { Column: { } column, DataContext: RowItem row } cell
            || !MultiValueColumns.TryGetValue(column, out var name)) return;
        var grid = FindParent<DataGrid>(cell);
        if (grid != null) OpenValueList(grid, cell, row, name);
    }

    private void OpenValueList(DataGrid grid, DataGridCell cell, RowItem row, string name)
    {
        if (DataContext is not SheetViewModel vm) return;
        grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true); // セルで入力中の値を先に確定する

        if (cell.Column != null && PairColumns.Contains(cell.Column))
        {
            // 組の列：2列いっしょの表（1行に1組）で入力する
            var (first, second) = (LinkedColumns.First, LinkedColumns.Second);
            ValueListEditor.OpenPairs(cell, first, second, row.GetPairs(first, second), (pairs, duplicates) =>
            {
                if (row.SetPairs(first, second, pairs)) vm.RequestSave();
                if (duplicates.Count > 0)
                    vm.Status = $"「{row.Key}」の「{first}」に同じ値が2回以上あります（そのまま保存しました）：{string.Join("、", duplicates)}";
            }, closed: () => cell.Focus());
            return;
        }

        ValueListEditor.Open(cell, row.GetManualValues(name), (values, duplicates) =>
        {
            if (row.SetManualValues(name, values)) vm.RequestSave();
            if (duplicates.Count > 0)
                vm.Status = $"「{row.Key}」の「{name}」で重複していた値を1つにまとめました：{string.Join("、", duplicates)}";
        }, closed: () => cell.Focus()); // 表に戻ってキーボードで続けられるように
    }

    private static DataGridCell? FindCell(DataGridColumn column, object item) =>
        column.GetCellContent(item) is { } content ? FindParent<DataGridCell>(content) : null;

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
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
