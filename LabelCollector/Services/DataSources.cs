using System.IO;
using System.Text;
using ExcelDataReader;
using LabelCollector.Models;

namespace LabelCollector.Services;

/// <summary>A列の値を集めてくるデータ元</summary>
public interface IDataSource
{
    List<SourceRecord> Fetch();
}

/// <summary>フォルダ内のファイルを集める。キー＝相対パス、A列＝ファイル名</summary>
public class FolderDataSource(string folder, string pattern) : IDataSource
{
    public List<SourceRecord> Fetch()
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {folder}");

        return Directory
            .EnumerateFiles(folder, string.IsNullOrWhiteSpace(pattern) ? "*.*" : pattern, SearchOption.AllDirectories)
            .Select(f => new SourceRecord(Path.GetRelativePath(folder, f), Path.GetFileName(f)))
            .OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// フォルダ内のエクセルファイル（.xlsx / .xlsm / .xls）を読む。
/// 各シートの上のほうから「キー列名」と同じ文字のセルを探して見出し行とし、
/// その下の行の値をキーにする。A列＝どのファイル・シートにあったか。
/// </summary>
public class ExcelDataSource(string folder, string keyColumn) : IDataSource
{
    private static readonly string[] Extensions = [".xlsx", ".xlsm", ".xls"];

    /// <summary>見出し行を探す範囲（上から何行目まで）</summary>
    private const int HeaderSearchRows = 20;

    static ExcelDataSource()
    {
        // .xls（古い形式）の読み込みに Shift_JIS などの文字コードが必要
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public List<SourceRecord> Fetch()
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("エクセルのフォルダが指定されていません");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {folder}");
        if (string.IsNullOrWhiteSpace(keyColumn))
            throw new ArgumentException("キーにする列名が指定されていません");

        var files = Directory
            .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("~$")) // エクセルで開いている間にできる一時ファイル
            // 指定フォルダ直下のファイルを先に、サブフォルダのファイルを後に読む
            .OrderBy(f => Path.GetRelativePath(folder, f).Count(c => c is '\\' or '/'))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var list = new List<SourceRecord>();
        int sheetsWithKey = 0;
        foreach (var file in files)
        {
            try
            {
                sheetsWithKey += ReadFile(file, list);
            }
            catch (Exception ex)
            {
                // 1ファイル読めなくても他のファイルは続ける
                AppLogger.Instance.Warn($"エクセルを読めませんでした: {file} ({ex.Message})");
            }
        }

        if (files.Count > 0 && sheetsWithKey == 0)
            AppLogger.Instance.Warn($"列「{keyColumn}」が見つかるシートがありませんでした: {folder}");

        return list;
    }

    /// <returns>キー列が見つかったシートの数</returns>
    private int ReadFile(string file, List<SourceRecord> list)
    {
        // エクセルで開いたままでも読めるよう、共有モードで開く
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var relative = Path.GetRelativePath(folder, file);
        int found = 0;
        do
        {
            int keyIndex = -1;
            int rowNo = 0;
            while (reader.Read())
            {
                rowNo++;
                if (keyIndex < 0)
                {
                    // まだ見出し行が見つかっていない
                    keyIndex = FindKeyColumn(reader);
                    if (keyIndex < 0 && rowNo >= HeaderSearchRows) break;
                    if (keyIndex >= 0) found++;
                    continue;
                }

                if (keyIndex >= reader.FieldCount) continue;
                var key = CellText(reader.GetValue(keyIndex));
                if (key.Length == 0) continue; // キーが空の行は読み飛ばす
                list.Add(new SourceRecord(key, $"{relative} / {reader.Name}"));
            }
        } while (reader.NextResult());

        return found;
    }

    private int FindKeyColumn(IExcelDataReader reader)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(CellText(reader.GetValue(i)), keyColumn.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string CellText(object? value) => value switch
    {
        null => "",
        // 1.0 のような数値は "1" にする
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(),
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy/MM/dd") : dt.ToString("yyyy/MM/dd HH:mm:ss"),
        _ => Convert.ToString(value)?.Trim() ?? "",
    };
}
