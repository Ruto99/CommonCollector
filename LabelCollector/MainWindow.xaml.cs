using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AvalonDock.Layout;
using LabelCollector.Services;
using LabelCollector.ViewModels;

namespace LabelCollector;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // LayoutAnchorable の中身は DataContext を引き継がないので明示的に渡す
        SheetList.DataContext = _vm;
        PropertyView.DataContext = _vm;
        LogGrid.ItemsSource = AppLogger.Instance.Entries;

        // 新しいログが出たら最下行まで自動スクロール
        AppLogger.Instance.Entries.CollectionChanged += LogEntries_CollectionChanged;

        // ログ欄を×で閉じたとき、ツールバーのボタンの状態も合わせる
        LogAnchorable.IsVisibleChanged += (_, _) => LogToggle.IsChecked = LogAnchorable.IsVisible;

        _vm.ActivateRequested += Activate;

        // タブを切り替えたら、そのシートをプロパティに表示する
        Dock.ActiveContentChanged += (_, _) =>
        {
            if (Dock.ActiveContent is SheetViewModel sheet) _vm.SelectedSheet = sheet;
        };
    }

    private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || LogGrid.Items.Count == 0) return;
        LogGrid.ScrollIntoView(LogGrid.Items[^1]);
    }

    private void LogToggle_Click(object sender, RoutedEventArgs e)
    {
        if (LogToggle.IsChecked == true) LogAnchorable.Show();
        else LogAnchorable.Hide();
    }

    // ---- メニュー ----

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // 開くたびに、表示中かどうかのチェックを最新にする
    private void ViewMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        MenuShowSheets.IsChecked = SheetsAnchorable.IsVisible;
        MenuShowProperties.IsChecked = PropertiesAnchorable.IsVisible;
        MenuShowLog.IsChecked = LogAnchorable.IsVisible;
        MenuShowToolbar.IsChecked = MainToolBarTray.Visibility == Visibility.Visible;
    }

    private void MenuShowSheets_Click(object sender, RoutedEventArgs e)
    {
        if (SheetsAnchorable.IsVisible) SheetsAnchorable.Hide(); else SheetsAnchorable.Show();
    }

    private void MenuShowProperties_Click(object sender, RoutedEventArgs e)
    {
        if (PropertiesAnchorable.IsVisible) PropertiesAnchorable.Hide(); else PropertiesAnchorable.Show();
    }

    private void MenuShowLog_Click(object sender, RoutedEventArgs e)
    {
        if (LogAnchorable.IsVisible) LogAnchorable.Hide(); else LogAnchorable.Show();
    }

    private void MenuShowToolbar_Click(object sender, RoutedEventArgs e)
    {
        MainToolBarTray.Visibility = MainToolBarTray.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        MessageBox.Show(this,
            $"LabelCollector  Version {version?.ToString(3)}\n\n" +
            "フォルダやエクセルのデータを集めて表にまとめ、手入力の列を行に結びつけて管理します。\n\n" +
            $"中間データ: {_vm.XmlPath}\nログ: {AppLogger.Instance.LogDirectory}",
            "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => AppLogger.Instance.Clear();

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLogger.Instance.LogDirectory);
            Process.Start(new ProcessStartInfo(AppLogger.Instance.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("ログフォルダを開けませんでした: " + ex.Message);
        }
    }

    // 既に開いているシートなら、そのタブを前面に出す
    private void Activate(SheetViewModel sheet)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var doc = Dock.Layout.Descendents().OfType<LayoutDocument>().FirstOrDefault(d => d.Content == sheet);
            if (doc != null) doc.IsActive = true;
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void SheetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SheetList.SelectedItem is SheetViewModel sheet) _vm.OpenSheet(sheet);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        AppLogger.Instance.Info("LabelCollector を終了します");
        _vm.Save();
        base.OnClosing(e);
    }
}
