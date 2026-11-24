using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelCollector;

/// <summary>
/// 手入力の列に複数の値を、1つずつ入力する一覧。表のセルのすぐ下に開く。
/// 値ごとに入力欄が分かれているので、区切りの文字（，など）を打たなくてよい。
/// ・Open：1つの列の値の一覧　・OpenPairs：組の2列（例：部品番号と数量）を、1行に1組で入力する表
/// ・Enter：次の欄・次の行へ（無ければ行を足す）　・空の行で Backspace：その行を消す　・↑↓：上下の行へ
/// ・Ctrl+Enter・「OK」・一覧の外をクリック：確定　・Esc・「キャンセル」：取り消し
/// ・エクセルなどからコピーした値を貼り付けると、1行（組の表なら1組）ずつに分かれる
/// ・1列目に同じ値が2回あると赤くする（Ctrl+Enter・OK では確定できない）
/// </summary>
public class ValueListEditor : Border
{
    private readonly int _columns;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _error = new()
    {
        Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        Visibility = Visibility.Collapsed,
    };
    private static readonly Brush DuplicateBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));

    private readonly Popup _popup;
    private readonly Action<List<string[]>, IReadOnlyList<string>> _commit;
    private Action? _closed;
    private bool _canceled;

    /// <param name="headers">組の表なら列の見出し（2つ）。1つの列なら null</param>
    /// <param name="commit">確定したときに呼ぶ（行ごとの値, 1列目で重複している値）</param>
    private ValueListEditor(FrameworkElement target, string[]? headers, IEnumerable<string[]> rows,
                            Action<List<string[]>, IReadOnlyList<string>> commit)
    {
        _columns = headers?.Length ?? 1;
        _commit = commit;
        Background = SystemColors.WindowBrush;
        BorderBrush = SystemColors.ActiveBorderBrush;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6);
        Width = Math.Max(target.ActualWidth, _columns == 1 ? 260 : 380);

        var hint = new TextBlock
        {
            Text = _columns == 1
                ? "1行に1つ。Enter で次の行、Ctrl+Enter で確定、Esc で取り消し"
                : "1行に1組。Enter で右の欄・次の行、Ctrl+Enter で確定、Esc で取り消し",
            Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        };

        var add = new Button { Content = _columns == 1 ? "＋ 値を追加" : "＋ 組を追加", Padding = new Thickness(6, 1, 6, 1), Focusable = false };
        add.Click += (_, _) => AddRow([], _rows.Children.Count)[0].Focus();
        var ok = new Button { Content = "OK", MinWidth = 64, Margin = new Thickness(0, 0, 4, 0), Focusable = false };
        ok.Click += (_, _) => Close(commit: true);
        var cancel = new Button { Content = "キャンセル", MinWidth = 64, Focusable = false };
        cancel.Click += (_, _) => Close(commit: false);

        var buttons = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(add);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var panel = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        panel.Children.Add(hint);
        if (headers != null)
        {
            // 組の表の見出し（入力欄と同じ並び）
            var header = CreateRowPanel(headers.Select(h => (UIElement)new TextBlock { Text = h, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 0, 0, 2) }).ToArray(),
                                        new TextBlock(), new Border { Width = 22 });
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);
        }
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_error, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(_error);
        panel.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300 });
        Child = panel;

        foreach (var r in rows) AddRow(r, _rows.Children.Count);
        if (_rows.Children.Count == 0) AddRow([], 0);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Close(commit: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close(commit: false);
                e.Handled = true;
            }
        };

        _popup = new Popup
        {
            Child = this,
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            StaysOpen = false, // 外をクリックしたら閉じる（確定）
        };
        _popup.Opened += (_, _) =>
        {
            // 最後の行の1つ目の欄の末尾から入力できるようにする
            var last = BoxesOf(_rows.Children.Count - 1)[0];
            last.Focus();
            Keyboard.Focus(last);
            last.CaretIndex = last.Text.Length;
        };
        _popup.Closed += (_, _) =>
        {
            if (!_canceled)
            {
                var values = Values();
                var duplicates = values.Select(v => v[0]).Where(v => v.Length > 0)
                                       .GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                _commit(values, duplicates);
            }
            _closed?.Invoke(); // 確定・取り消しのどちらでも
        };
        Check();
    }

    /// <summary>target（表のセル）の下に、1つの列の値の一覧を開く</summary>
    /// <param name="commit">確定したときに呼ぶ（値の一覧, 重複していて1つにまとめた値）。取り消したときは呼ばない</param>
    /// <param name="closed">閉じたときに呼ぶ（確定・取り消しのどちらでも。表にフォーカスを戻すのに使う）</param>
    public static void Open(FrameworkElement target, IReadOnlyList<string> values,
                            Action<IReadOnlyList<string>, IReadOnlyList<string>> commit, Action? closed = null)
    {
        var editor = new ValueListEditor(target, null, values.Select(v => new[] { v }),
            (rows, duplicates) => commit(rows.Select(r => r[0]).Distinct().ToList(), duplicates)) { _closed = closed };
        editor._popup.IsOpen = true;
    }

    /// <summary>target（表のセル）の下に、組の2列を1行に1組で入力する表を開く</summary>
    /// <param name="commit">確定したときに呼ぶ（組の一覧, 1列目で重複している値）。取り消したときは呼ばない。
    /// 重複していても組はまとめない（2列目の値が違うかもしれないので）</param>
    public static void OpenPairs(FrameworkElement target, string header1, string header2,
                                 IReadOnlyList<(string First, string Second)> pairs,
                                 Action<IReadOnlyList<(string First, string Second)>, IReadOnlyList<string>> commit,
                                 Action? closed = null)
    {
        var editor = new ValueListEditor(target, [header1, header2], pairs.Select(p => new[] { p.First, p.Second }),
            (rows, duplicates) => commit(rows.Select(r => (r[0], r[1])).ToList(), duplicates)) { _closed = closed };
        editor._popup.IsOpen = true;
    }

    private void Close(bool commit)
    {
        if (commit && !Check()) return; // 赤くなっている値を直してから
        _canceled = !commit;
        _popup.IsOpen = false;
    }

    /// <summary>index 行目の入力欄</summary>
    private TextBox[] BoxesOf(int index) => (TextBox[])((FrameworkElement)_rows.Children[index]).Tag;

    private IEnumerable<TextBox[]> AllBoxes() => _rows.Children.OfType<FrameworkElement>().Select(r => (TextBox[])r.Tag);

    /// <summary>入力された値（前後の空白を取り、全部の欄が空の行は除く）</summary>
    private List<string[]> Values() =>
        AllBoxes().Select(b => b.Select(x => x.Text.Trim()).ToArray()).Where(v => v.Any(x => x.Length > 0)).ToList();

    /// <summary>1行分：番号・入力欄（列の数だけ、同じ幅）・× ボタン</summary>
    private Grid CreateRowPanel(UIElement[] cells, UIElement number, UIElement remove)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        foreach (var _ in cells) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(number, 0);
        grid.Children.Add(number);
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i + 1);
            if (cells[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(4, fe.Margin.Top, 0, fe.Margin.Bottom);
            grid.Children.Add(cells[i]);
        }
        Grid.SetColumn(remove, cells.Length + 1);
        grid.Children.Add(remove);
        return grid;
    }

    /// <summary>index の位置に行を足して、その入力欄を返す</summary>
    private TextBox[] AddRow(string[] values, int index)
    {
        var boxes = Enumerable.Range(0, _columns)
            .Select(i => new TextBox
            {
                Text = i < values.Length ? values[i] : "",
                Padding = new Thickness(2, 1, 2, 1), VerticalContentAlignment = VerticalAlignment.Center,
            }).ToArray();
        var remove = new Button
        {
            Content = "×", Width = 22, Margin = new Thickness(4, 0, 0, 0), Focusable = false,
            ToolTip = _columns == 1 ? "この値を消す" : "この組を消す",
        };
        var number = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };

        var row = CreateRowPanel(boxes.Cast<UIElement>().ToArray(), number, remove);
        row.Tag = boxes;

        remove.Click += (_, _) => RemoveRow(row, 0);
        for (int c = 0; c < boxes.Length; c++)
        {
            var column = c;
            var box = boxes[c];
            box.TextChanged += (_, _) => Check();
            box.PreviewKeyDown += (_, e) => Box_PreviewKeyDown(row, column, e);
            DataObject.AddPastingHandler(box, (_, e) => Box_Pasting(row, column, box, e));
        }

        _rows.Children.Insert(index, row);
        Renumber();
        Check();
        return boxes;
    }

    private void RemoveRow(FrameworkElement row, int column)
    {
        var index = _rows.Children.IndexOf(row);
        _rows.Children.Remove(row);
        if (_rows.Children.Count == 0) AddRow([], 0);
        Renumber();
        Check();
        var focus = BoxesOf(Math.Max(0, Math.Min(index, _rows.Children.Count) - 1))[column];
        focus.Focus();
        focus.CaretIndex = focus.Text.Length;
    }

    private void Renumber()
    {
        var i = 1;
        foreach (var row in _rows.Children.OfType<Grid>())
            ((TextBlock)row.Children[0]).Text = $"{i++}.";
    }

    private void Box_PreviewKeyDown(FrameworkElement row, int column, KeyEventArgs e)
    {
        var index = _rows.Children.IndexOf(row);
        var boxes = BoxesOf(index);
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (column + 1 < _columns)
            {
                boxes[column + 1].Focus(); // 組の右の欄へ
            }
            else
            {
                // 次の行が空ならそこへ、無ければ行を足して移る
                var next = index + 1 < _rows.Children.Count ? BoxesOf(index + 1) : null;
                (next != null && next.All(b => b.Text.Length == 0) ? next : AddRow([], index + 1))[0].Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Back && boxes.All(b => b.Text.Length == 0) && _rows.Children.Count > 1)
        {
            if (column > 0) boxes[column - 1].Focus(); // 組の左の欄へ戻る
            else RemoveRow(row, _columns - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && index + 1 < _rows.Children.Count)
        {
            BoxesOf(index + 1)[column].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && index > 0)
        {
            BoxesOf(index - 1)[column].Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 改行・タブを含む文字を貼り付けたら、行に分ける。
    /// 組の表では、エクセルの2列（タブ区切り）を貼ると、そのまま組として入る
    /// </summary>
    private void Box_Pasting(FrameworkElement row, int column, TextBox box, DataObjectPastingEventArgs e)
    {
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text) return;
        if (text.IndexOfAny(['\r', '\n', '\t']) < 0) return; // 1つだけなら普通の貼り付け

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Split('\t').Select(p => p.Trim()).ToArray())
                        .Where(l => l.Any(p => p.Length > 0)).ToList();
        if (_columns == 1) // 1つの列なら、タブで区切られた値も1つずつの行にする
            lines = lines.SelectMany(l => l.Where(p => p.Length > 0).Select(p => new[] { p })).ToList();
        if (lines.Count == 0) return;
        e.CancelCommand();

        // 1行目は今の行のカーソルの欄から、残りは次の行から
        var index = _rows.Children.IndexOf(row);
        TextBox last = box;
        for (int i = 0; i < lines.Count; i++)
        {
            var boxes = i == 0 ? BoxesOf(index) : AddRow([], index + i);
            for (int c = 0; c < lines[i].Length && column + c < _columns; c++)
            {
                last = boxes[column + c];
                last.Text = lines[i][c];
            }
        }
        last.Focus();
        last.CaretIndex = last.Text.Length;
    }

    /// <summary>1列目に同じ値が2回以上ある入力欄を赤くして、メッセージを出す。問題が無ければ true</summary>
    private bool Check()
    {
        var firsts = AllBoxes().Select(b => b[0]).ToList();
        var duplicates = firsts.Select(b => b.Text.Trim()).Where(v => v.Length > 0)
                               .GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var b in firsts)
        {
            // 入力中の欄は枠の色がテーマの色になるので、背景の色でも分かるようにする
            if (duplicates.Contains(b.Text.Trim()))
            {
                b.BorderBrush = Brushes.Red;
                b.Background = DuplicateBackground;
            }
            else
            {
                b.ClearValue(Control.BorderBrushProperty); // テーマの既定の枠・背景に戻す
                b.ClearValue(Control.BackgroundProperty);
            }
        }

        _error.Text = duplicates.Count > 0 ? $"同じ値が2回以上入っています：{string.Join("、", duplicates)}" : "";
        _error.Visibility = duplicates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return duplicates.Count == 0;
    }
}
