using System.IO;
using LabelCollector.Models;
using LabelCollector.Models.Sources;

namespace LabelCollector.Services.Sources;

/// <summary>フォルダ内のファイルの一覧を読む。キー＝フォルダからの相対パス</summary>
public class FileListReader(FileListSourceSettings settings) : IDataSourceReader
{
    public const string ColumnFileName = "ファイル名";
    public const string ColumnExtension = "拡張子";
    public const string ColumnFolder = "フォルダ";
    public const string ColumnModified = "更新日時";
    public const string ColumnSize = "サイズ";

    private static readonly string[] AllColumns = [ColumnFileName, ColumnExtension, ColumnFolder, ColumnModified, ColumnSize];

    public List<SourceRecord> Read()
    {
        var folder = settings.FolderPath;
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("フォルダが指定されていません");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {folder}");

        var pattern = string.IsNullOrWhiteSpace(settings.FilePattern) ? "*.*" : settings.FilePattern;
        var option = settings.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var mappings = settings.Columns.Where(m => m.TargetName.Length > 0).ToList();

        return Directory.EnumerateFiles(folder, pattern, option)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f =>
            {
                var info = new FileInfo(f);
                var relative = Path.GetRelativePath(folder, f);
                var values = new Dictionary<string, string>();
                foreach (var m in mappings) values[m.TargetName] = Value(info, relative, m.SourceColumn.Trim());
                return new SourceRecord(relative, relative, values);
            })
            .ToList();
    }

    public IReadOnlyList<string> GetColumnNames() => AllColumns;

    private static string Value(FileInfo info, string relative, string column) => column switch
    {
        ColumnFileName => info.Name,
        ColumnExtension => info.Extension,
        ColumnFolder => Path.GetDirectoryName(relative) ?? "",
        ColumnModified => info.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"),
        ColumnSize => info.Length.ToString(),
        _ => "",
    };
}
