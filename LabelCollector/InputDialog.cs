using System.Windows;
using System.Windows.Controls;

namespace LabelCollector;

/// <summary>1行の文字を入力してもらう小さなダイアログ</summary>
public class InputDialog : Window
{
    private readonly TextBox _box;

    private InputDialog(string title, string message, string initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 10), Padding = new Thickness(3, 2, 3, 2) };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "キャンセル", IsCancel = true, MinWidth = 80 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            _box.Focus();
            // 拡張子の前までを選択しておく
            var dot = _box.Text.LastIndexOf('.');
            _box.Select(0, dot > 0 ? dot : _box.Text.Length);
        };
    }

    /// <returns>入力された文字。キャンセルなら null</returns>
    public static string? Show(Window owner, string title, string message, string initial = "")
    {
        var dlg = new InputDialog(title, message, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg._box.Text : null;
    }
}
