using System.IO;
using Microsoft.Win32;
using LabelCollector.Models;
using LabelCollector.Services;

namespace LabelCollector.ViewModels;

/// <summary>データ元の追加・場所の選択など、画面から使う共通の操作</summary>
public static class SourceEditing
{
    /// <summary>
    /// データ元の場所（ファイル or フォルダ）をダイアログで選ぶ。
    /// IFileLocation / IFolderLocation を実装した設定なら、種類ごとの画面コードなしで使える
    /// </summary>
    /// <returns>選んだら true。キャンセルなら false。場所を持たない種類なら true</returns>
    public static bool PickLocation(SourceSettings settings)
    {
        switch (settings)
        {
            case IFileLocation file:
            {
                var dlg = new OpenFileDialog { Title = "ファイルを選択", Filter = file.FileDialogFilter, CheckFileExists = true };
                var dir = Path.GetDirectoryName(file.FilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                if (dlg.ShowDialog() != true) return false;
                file.FilePath = dlg.FileName;
                return true;
            }
            case IFolderLocation folder:
            {
                var dlg = new OpenFolderDialog { Title = "フォルダを選択" };
                if (Directory.Exists(folder.FolderPath)) dlg.InitialDirectory = folder.FolderPath;
                if (dlg.ShowDialog() != true) return false;
                folder.FolderPath = dlg.FolderName;
                return true;
            }
            default:
                return true;
        }
    }

    /// <summary>新しいデータ元を作って、場所を選んでもらう。キャンセルなら null</summary>
    public static SourceSettings? CreateSource(DataSourceKind kind)
    {
        var settings = kind.Create();
        return PickLocation(settings) ? settings : null;
    }

    /// <summary>データ元の場所から、シート名の候補を作る（フォルダ名・ファイル名）</summary>
    public static string SuggestName(SourceSettings settings, string fallback)
    {
        var location = settings.Location.TrimEnd('\\', '/');
        var name = settings is IFileLocation ? Path.GetFileNameWithoutExtension(location) : Path.GetFileName(location);
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    /// <summary>
    /// データ元の見出しなどから取り込める列を読み、まだ取り込んでいない列を追加する
    /// </summary>
    /// <returns>追加した列の数</returns>
    public static int AddAvailableColumns(SourceSettings settings)
    {
        var kind = DataSourceRegistry.Find(settings)
                   ?? throw new InvalidOperationException($"未対応のデータ元です: {settings.GetType().Name}");
        var names = kind.CreateReader(settings).GetColumnNames();
        var keys = settings is TableSourceSettings t ? t.GetKeyColumnNames() : [];
        int added = 0;
        foreach (var name in names)
        {
            if (keys.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (settings.Columns.Any(c => string.Equals(c.SourceColumn.Trim(), name, StringComparison.OrdinalIgnoreCase))) continue;
            settings.Columns.Add(new ColumnMapping(name));
            added++;
        }
        return added;
    }
}
