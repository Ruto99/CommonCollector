using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LabelCollector.Models;
using LabelCollector.Services;
using LabelCollector.ViewModels;

namespace LabelCollector;

/// <summary>
/// 右側のプロパティウィンドウ。中間データ（.lc）の中身や、シートの設定を編集する。
/// ここのイベント処理はデータ元の種類によらない共通のもの（一覧への追加・削除、場所の選択、保存）。
/// </summary>
public partial class PropertyPanel : UserControl
{
    public PropertyPanel()
    {
        InitializeComponent();
    }

    // ---- 保存 ----

    /// <summary>画面の部品が属しているシートを探す（上にたどって最初に見つかった SheetViewModel）</summary>
    private static SheetViewModel? FindSheet(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is FrameworkElement { DataContext: SheetViewModel sheet }) return sheet;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>その部品が属しているシートを保存する（列の構成も更新される）</summary>
    private static void SaveFrom(object sender) => FindSheet(sender as DependencyObject)?.RequestSave();

    // 文字の入力が終わったら保存
    private void Setting_LostFocus(object sender, RoutedEventArgs e) => SaveFrom(sender);

    // チェックボックスなどを変えたら保存
    private void Setting_Changed(object sender, RoutedEventArgs e) => SaveFrom(sender);

    // ---- 一覧の項目（キー列名・取り込む列・手入力の列）----

    /// <summary>× ボタン：その項目を、入っている一覧から削除する</summary>
    private void RemoveListItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        var sheet = FindSheet(element); // 削除すると画面から外れるので、先に探しておく

        DependencyObject? d = element;
        while (d != null && d is not ItemsControl) d = VisualTreeHelper.GetParent(d);
        if (d is ItemsControl { ItemsSource: IList list }) list.Remove(element.DataContext);

        sheet?.RequestSave();
    }

    private void AddKeyColumn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TableSourceSettings settings) return;
        settings.KeyColumns.Add(new TextItem());
        SaveFrom(sender);
    }

    private void AddColumn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SourceSettings settings) return;
        settings.Columns.Add(new ColumnMapping());
        SaveFrom(sender);
    }

    /// <summary>データ元の見出しなどから、取り込める列をまとめて追加する</summary>
    private void LoadColumns_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SourceSettings settings) return;
        try
        {
            var added = SourceEditing.AddAvailableColumns(settings);
            if (added == 0)
            {
                MessageBox.Show(Window.GetWindow(this)!,
                    "追加できる列がありませんでした。\n場所とキー列名が正しいか、すでに全部取り込んでいないか確認してください。",
                    "列の候補を読み込む", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AppLogger.Instance.Info($"列の候補を {added} 列追加しました: {settings.Location}");
            SaveFrom(sender);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this)!, "列の候補を読み込めませんでした。\n" + ex.Message,
                            "列の候補を読み込む", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddManualColumn_Click(object sender, RoutedEventArgs e)
    {
        if (FindSheet(sender as DependencyObject) is not { } sheet) return;
        sheet.ManualColumns.Add(new TextItem());
        sheet.RequestSave();
    }

    // ---- データ元 ----

    /// <summary>「…」ボタン：ファイルやフォルダを選ぶ（IFileLocation / IFolderLocation を実装した設定）</summary>
    private void BrowseLocation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SourceSettings settings) return;
        if (SourceEditing.PickLocation(settings)) SaveFrom(sender);
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SourceSettings settings) return;
        if (FindSheet(sender as DependencyObject) is not { } sheet) return;

        var ok = MessageBox.Show(Window.GetWindow(this)!,
            $"データ元「{settings.Location}」を削除します。よろしいですか？\n（表の行は、次に更新したときに「消えた行」になります）",
            "データ元の削除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok == MessageBoxResult.Yes) sheet.RemoveSource(settings);
    }

    /// <summary>「＋ データ元を追加」：登録されている種類をメニューで出して、選んだものを追加する</summary>
    private void AddSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindSheet(button) is not { } sheet) return;

        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var kind in DataSourceRegistry.Kinds)
        {
            var item = new MenuItem { Header = $"{kind.Icon} {kind.DisplayName}..." };
            item.Click += (_, _) =>
            {
                if (SourceEditing.CreateSource(kind) is { } source) sheet.AddSource(source);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
