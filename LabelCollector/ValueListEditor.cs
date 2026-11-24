using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LabelCollector;

/// <summary>
/// 「複数の値」の手入力の列の値を、1つずつ入力する一覧。表のセルのすぐ下に開く（Open で開く）。
/// 値ごとに入力欄が分かれているので、区切りの文字（，など）を打たなくてよい。
/// ・Enter：次の行を足して移る　・空の行で Backspace：その行を消す　・↑↓：行を移る
/// ・Ctrl+Enter・「OK」・一覧の外をクリック：確定　・Esc・「キャンセル」：取り消し
/// ・エクセルなどから縦（または横）に並んだ値を貼り付けると、1つずつの行に分かれる
/// ・同じ値が2回あると赤くする（Ctrl+Enter・OK では確定できない。外をクリックして閉じたときは1つにまとめる）
/// </summary>
public class ValueListEditor : Border
{
    private readonly StackPanel _rows = new();
    private readonly TextBlock _error = new()
    {
        Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        Visibility = Visibility.Collapsed,
    };
    private readonly Brush _normalBorder = new TextBox().BorderBrush;
    private static readonly Brush DuplicateBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));

    private readonly Popup _popup;
    private readonly Action<IReadOnlyList<string>, IReadOnlyList<string>> _commit;
    private bool _canceled;

    /// <param name="commit">確定したときに呼ぶ（値の一覧, 重複していて1つにまとめた値）</param>
    private ValueListEditor(UIElement target, double width, IReadOnlyList<string> values,
                            Action<IReadOnlyList<string>, IReadOnlyList<string>> commit)
    {
        _commit = commit;
        Background = SystemColors.WindowBrush;
        BorderBrush = SystemColors.ActiveBorderBrush;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6);
        Width = Math.Max(width, 260);

        var hint = new TextBlock
        {
            Text = "1行に1つ。Enter で次の行、Ctrl+Enter で確定、Esc で取り消し",
            Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        };

        var add = new Button { Content = "＋ 値を追加", Padding = new Thickness(6, 1, 6, 1), Focusable = false };
        add.Click += (_, _) => AddRow("", _rows.Children.Count).Focus();
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
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_error, Dock.Bottom);
        panel.Children.Add(hint);
        panel.Children.Add(buttons);
        panel.Children.Add(_error);
        panel.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300 });
        Child = panel;

        foreach (var v in values) AddRow(v, _rows.Children.Count);
        if (_rows.Children.Count == 0) AddRow("", 0);

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
            // 最後の行の末尾から入力できるようにする
            var last = Boxes().Last();
            last.Focus();
            Keyboard.Focus(last);
            last.CaretIndex = last.Text.Length;
        };
        _popup.Closed += (_, _) =>
        {
            if (!_canceled)
            {
                var all = Values();
                var duplicates = all.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                _commit(all.Distinct().ToList(), duplicates);
            }
            Closed?.Invoke(); // 確定・取り消しのどちらでも
        };
        Check();
    }

    /// <summary>一覧が閉じた（確定・取り消しのどちらでも）</summary>
    private new Action? Closed { get; set; }

    /// <summary>target（表のセル）の下に一覧を開く</summary>
    /// <param name="commit">確定したときに呼ぶ（値の一覧, 重複していて1つにまとめた値）。取り消したときは呼ばない</param>
    /// <param name="closed">閉じたときに呼ぶ（確定・取り消しのどちらでも。表にフォーカスを戻すのに使う）</param>
    public static void Open(FrameworkElement target, IReadOnlyList<string> values,
                            Action<IReadOnlyList<string>, IReadOnlyList<string>> commit, Action? closed = null)
    {
        var editor = new ValueListEditor(target, target.ActualWidth, values, commit) { Closed = closed };
        editor._popup.IsOpen = true;
    }

    private void Close(bool commit)
    {
        if (commit && !Check()) return; // 赤くなっている値を直してから
        _canceled = !commit;
        _popup.IsOpen = false;
    }

    private IEnumerable<TextBox> Boxes() => _rows.Children.OfType<DockPanel>().Select(r => (TextBox)r.Tag);

    /// <summary>入力された値（前後の空白を取り、空の行は除く）</summary>
    private List<string> Values() => Boxes().Select(b => b.Text.Trim()).Where(v => v.Length > 0).ToList();

    /// <summary>index の位置に行を足して、その入力欄を返す</summary>
    private TextBox AddRow(string value, int index)
    {
        var box = new TextBox { Text = value, Padding = new Thickness(2, 1, 2, 1), VerticalContentAlignment = VerticalAlignment.Center };
        var remove = new Button { Content = "×", Width = 22, Margin = new Thickness(4, 0, 0, 0), ToolTip = "この値を消す", Focusable = false };
        var number = new TextBlock { Width = 22, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2), Tag = box };
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

    /// <summary>同じ値が2回以上ある入力欄を赤くして、メッセージを出す。問題が無ければ true</summary>
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
}
