using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        ExplorerTree.DataContext = _vm;
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

    // ---- 左のエクスプローラー ----

    private object? SelectedTreeItem => ExplorerTree.SelectedItem;

    private void ExplorerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // シートを選んだら、右のプロパティに表示する
        if (e.NewValue is SheetViewModel sheet) _vm.SelectedSheet = sheet;
    }

    // ダブルクリック：シートならタブで開く、中間データ(XML)なら開いて入れ替える
    private void ExplorerTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderMouse(e) is not { } item) return;
        switch (item.DataContext)
        {
            case SheetViewModel sheet:
                _vm.OpenSheet(sheet);
                e.Handled = true;
                break;
            case XmlFileNode xml when !xml.IsCurrent:
                _vm.OpenXmlFile(xml.FullPath);
                e.Handled = true;
                break;
        }
    }

    // 右クリックでも項目を選択する（標準の TreeView は左クリックでしか選択しない）
    private void ExplorerTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnderMouse(e) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private TreeViewItem? ItemUnderMouse(MouseEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d != null && d is not TreeViewItem)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as TreeViewItem;
    }

    // 選んでいる項目の種類に合わせて、右クリックメニューの項目を出し分ける
    private void ExplorerTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = SelectedTreeItem;
        var xml = item as XmlFileNode;
        var isFolder = item is FolderNode || item == null;
        var isSheet = item is SheetViewModel;

        SetVisible(CtxOpenXml, xml is { IsCurrent: false });
        SetVisible(CtxCreateXml, isFolder || xml != null);
        SetVisible(CtxDeleteXml, xml != null);
        CtxDeleteXml.IsEnabled = xml is { IsCurrent: false };
        SetVisible(CtxAddExcel, xml is { IsCurrent: true });
        SetVisible(CtxAddFolder, xml is { IsCurrent: true });
        SetVisible(CtxOpenSheet, isSheet);
        SetVisible(CtxRemoveSheet, isSheet);
    }

    private static void SetVisible(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>選んでいる項目があるフォルダ（未選択なら作業フォルダ）</summary>
    private string SelectedFolder => SelectedTreeItem switch
    {
        ExplorerNode node => node.FolderPath,
        _ => _vm.Explorer.RootPath,
    };

    private void CtxOpenXml_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTreeItem is XmlFileNode xml) _vm.OpenXmlFile(xml.FullPath);
    }

    private void CtxCreateXml_Click(object sender, RoutedEventArgs e)
    {
        var folder = SelectedFolder;
        var name = InputDialog.Show(this, "新しい中間データ(XML)を作成",
            $"作成するファイルの名前を入力してください。\n作成先: {folder}", "新しい中間データ.xml");
        if (name != null) _vm.CreateXml(folder, name);
    }

    private void CtxDeleteXml_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTreeItem is XmlFileNode xml) _vm.DeleteXml(xml.FullPath);
    }

    private void CtxShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // ファイルなら、そのファイルを選んだ状態で開く
            var psi = SelectedTreeItem is XmlFileNode xml
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{xml.FullPath}\"")
                : new ProcessStartInfo(SelectedFolder) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("エクスプローラーで開けませんでした: " + ex.Message);
        }
    }

    private void CtxReload_Click(object sender, RoutedEventArgs e) => _vm.Explorer.Reload();

    protected override void OnClosing(CancelEventArgs e)
    {
        AppLogger.Instance.Info("LabelCollector を終了します");
        _vm.Save();
        base.OnClosing(e);
    }
}
