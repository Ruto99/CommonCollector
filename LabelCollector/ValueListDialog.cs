using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelCollector;

/// <summary>
/// 「複数の値」の手入力の列の値を、1つずつ入力する画面。
/// 値ごとに入力欄が分かれているので、区切りの文字（，など）を打たなくてよい。
/// ・Enter：次の行を足して移る　・空の行で Backspace：その行を消す　・Ctrl+Enter：OK
/// ・エクセルなどから縦（または横）に並んだ値を貼り付けると、1つずつの行に分かれる
/// ・同じ値が2回あると赤枠にして、OK できないようにする
/// </summary>
public class ValueListDialog : Window
{
    private readonly StackPanel _rows = new();
    private readonly TextBlock _error = new()
    {
        Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        Visibility = Visibility.Collapsed,
    };
    private readonly Brush _normalBorder;
    private static readonly Brush DuplicateBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));

    private ValueListDialog(string column, string key, IReadOnlyList<string> values)
    {
        Title = $"値の一覧 - {column}";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 600;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _normalBorder = new TextBox().BorderBrush;

        var message = new TextBlock
        {
            Text = $"キー「{key}」の「{column}」。1行に1つずつ入力します。\nEnter で次の行、空の行で Backspace でその行を消します。Ctrl+Enter で OK。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var add = new Button { Content = "＋ 値を追加", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) => AddRow("", _rows.Children.Count).Focus();

        var ok = new Button { Content = "OK", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => TryClose();
        var cancel = new Button { Content = "キャンセル", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(message, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_error, Dock.Bottom);
        DockPanel.SetDock(add, Dock.Bottom);
        panel.Children.Add(message);
        panel.Children.Add(buttons);
        panel.Children.Add(_error);
        panel.Children.Add(add);
        panel.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380 });
        Content = panel;

        foreach (var v in values) AddRow(v, _rows.Children.Count);
        if (_rows.Children.Count == 0) AddRow("", 0);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                TryClose();
                e.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            // 最後の行の末尾から入力できるようにする
            var last = Boxes().Last();
            last.Focus();
            last.CaretIndex = last.Text.Length;
        };
        Check();
    }

    /// <returns>入力された値の一覧（前後の空白を取り、空の行は除く）。キャンセルなら null</returns>
    public static IReadOnlyList<string>? Show(Window owner, string column, string key, IReadOnlyList<string> values)
    {
        var dlg = new ValueListDialog(column, key, values) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Values() : null;
    }

    private IEnumerable<TextBox> Boxes() => _rows.Children.OfType<DockPanel>().Select(r => (TextBox)r.Tag);

    private List<string> Values() => Boxes().Select(b => b.Text.Trim()).Where(v => v.Length > 0).ToList();

    /// <summary>index の位置に行を足して、その入力欄を返す</summary>
    private TextBox AddRow(string value, int index)
    {
        var box = new TextBox { Text = value, Padding = new Thickness(3, 2, 3, 2), VerticalContentAlignment = VerticalAlignment.Center };
        var remove = new Button { Content = "×", Width = 24, Margin = new Thickness(4, 0, 0, 0), ToolTip = "この値を消す", Focusable = false };
        var number = new TextBlock { Width = 26, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3), Tag = box };
        DockPanel.SetDock(number, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(number);
        row.Children.Add(remove);
        row.Children.Add(box);

        remove.Click += (_, _) => RemoveRow(row);
        box.TextChanged += (_, _) => Check();
        box.PreviewKeyDown += (_, e) => Box_PreviewKeyDown(row, box, e);
        DataObject.AddPastingHandler(box, (_, e) => Box_Pasting(row, box, e));

        _rows.Children.Insert(index, row);
        Renumber();
        Check();
        return box;
    }

    private void RemoveRow(DockPanel row)
    {
        var index = _rows.Children.IndexOf(row);
        _rows.Children.Remove(row);
        if (_rows.Children.Count == 0) AddRow("", 0);
        Renumber();
        Check();
        var focus = Boxes().ElementAt(Math.Max(0, Math.Min(index, _rows.Children.Count) - 1));
        focus.Focus();
        focus.CaretIndex = focus.Text.Length;
    }

    private void Renumber()
    {
        var i = 1;
        foreach (var row in _rows.Children.OfType<DockPanel>())
            ((TextBlock)row.Children[0]).Text = $"{i++}.";
    }

    private void Box_PreviewKeyDown(DockPanel row, TextBox box, KeyEventArgs e)
    {
        var index = _rows.Children.IndexOf(row);
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            // 次の行が空ならそこへ、無ければ行を足して移る
            var next = index + 1 < _rows.Children.Count ? Boxes().ElementAt(index + 1) : null;
            (next is { Text.Length: 0 } ? next : AddRow("", index + 1)).Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && box.Text.Length == 0 && _rows.Children.Count > 1)
        {
            RemoveRow(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && index + 1 < _rows.Children.Count)
        {
            Boxes().ElementAt(index + 1).Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && index > 0)
        {
            Boxes().ElementAt(index - 1).Focus();
            e.Handled = true;
        }
    }

    /// <summary>改行・タブを含む文字を貼り付けたら、1つずつの行に分ける</summary>
    private void Box_Pasting(DockPanel row, TextBox box, DataObjectPastingEventArgs e)
    {
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text) return;
        var parts = text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count <= 1) return; // 1つだけなら普通の貼り付け
        e.CancelCommand();

        // 1つ目はカーソルの位置に、残りは次の行から
        box.SelectedText = parts[0];
        box.CaretIndex = box.SelectionStart + box.SelectionLength;
        box.SelectionLength = 0;
        var index = _rows.Children.IndexOf(row);
        TextBox last = box;
        foreach (var p in parts.Skip(1)) last = AddRow(p, ++index);
        last.Focus();
        last.CaretIndex = last.Text.Length;
    }

    /// <summary>同じ値が2回以上ある入力欄を赤枠にして、メッセージを出す。問題が無ければ true</summary>
    private bool Check()
    {
        var boxes = Boxes().ToList();
        var duplicates = boxes.Select(b => b.Text.Trim()).Where(v => v.Length > 0)
                              .GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var b in boxes)
        {
            // 入力中の欄は枠の色がテーマの色になるので、背景の色でも分かるようにする
            var dup = duplicates.Contains(b.Text.Trim());
            b.BorderBrush = dup ? Brushes.Red : _normalBorder;
            b.Background = dup ? DuplicateBackground : Brushes.White;
        }

        _error.Text = duplicates.Count > 0 ? $"同じ値が2回以上入っています：{string.Join("、", duplicates)}" : "";
        _error.Visibility = duplicates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return duplicates.Count == 0;
    }

    private void TryClose()
    {
        if (!Check()) return; // 赤枠の値を直してから
        DialogResult = true;
    }
}
