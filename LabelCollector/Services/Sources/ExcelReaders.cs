using System.IO;
using System.Text;
using ExcelDataReader;
using LabelCollector.Models;
using LabelCollector.Models.Sources;

namespace LabelCollector.Services.Sources;

/// <summary>エクセルファイル 1つを読む</summary>
public class ExcelFileReader(ExcelFileSourceSettings settings) : IDataSourceReader
{
    public List<SourceRecord> Read()
    {
        if (string.IsNullOrWhiteSpace(settings.FilePath))
            throw new ArgumentException("エクセルファイルが指定されていません");
        if (!File.Exists(settings.FilePath))
            throw new FileNotFoundException($"ファイルが見つかりません: {settings.FilePath}");

        var table = new ExcelTable(settings);
        var list = new List<SourceRecord>();
        table.ReadFile(settings.FilePath, Path.GetFileName(settings.FilePath), list);
        table.WarnIfNothingFound(settings.FilePath);
        return list;
    }

    public IReadOnlyList<string> GetColumnNames() =>
        File.Exists(settings.FilePath) ? new ExcelTable(settings).ReadHeaders([settings.FilePath]) : [];
}

/// <summary>フォルダ内のエクセルファイル（.xlsx / .xlsm / .xls）をまとめて読む</summary>
public class ExcelFolderReader(ExcelFolderSourceSettings settings) : IDataSourceReader
{
    public List<SourceRecord> Read()
    {
        if (string.IsNullOrWhiteSpace(settings.FolderPath))
            throw new ArgumentException("エクセルのフォルダが指定されていません");
        if (!Directory.Exists(settings.FolderPath))
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {settings.FolderPath}");

        var table = new ExcelTable(settings);
        var list = new List<SourceRecord>();
        foreach (var file in EnumerateFiles())
        {
            try
            {
                table.ReadFile(file, Path.GetRelativePath(settings.FolderPath, file), list);
            }
            catch (Exception ex)
            {
                // 1ファイル読めなくても他のファイルは続ける
                AppLogger.Instance.Warn($"エクセルを読めませんでした: {file} ({ex.Message})");
            }
        }
        table.WarnIfNothingFound(settings.FolderPath);
        return list;
    }

    public IReadOnlyList<string> GetColumnNames() =>
        Directory.Exists(settings.FolderPath) ? new ExcelTable(settings).ReadHeaders(EnumerateFiles()) : [];

    private List<string> EnumerateFiles()
    {
        var folder = settings.FolderPath;
        var option = settings.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .EnumerateFiles(folder, "*.*", option)
            .Where(ExcelTable.IsExcelFile)
            // 指定フォルダ直下のファイルを先に、サブフォルダのファイルを後に読む
            .OrderBy(f => Path.GetRelativePath(folder, f).Count(c => c is '\\' or '/'))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// エクセルの表を読む共通処理。
/// 各シートの上のほうから、キー列名の候補のどれかと同じ文字のセルを探して見出し行とし、
/// その下の行を読む。1つの見出し行に複数の候補があるときは、候補の順番が先のものをキーにする。
/// 取り込む列は、同じ見出し行から列名で探す。
/// </summary>
internal class ExcelTable
{
    private static readonly string[] Extensions = [".xlsx", ".xlsm", ".xls"];

    /// <summary>見出し行を探す範囲（上から何行目まで）</summary>
    private const int HeaderSearchRows = 20;

    private readonly TableSourceSettings _settings;
    private readonly List<string> _keyColumns;
    private readonly List<ColumnMapping> _mappings;
    private readonly HashSet<string> _foundColumns = new(StringComparer.OrdinalIgnoreCase);
    private int _sheetsWithKey;

    static ExcelTable()
    {
        // .xls（古い形式）の読み込みに Shift_JIS などの文字コードが必要
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ExcelTable(TableSourceSettings settings)
    {
        _settings = settings;
        _keyColumns = settings.GetKeyColumnNames();
        _mappings = settings.Columns.Where(m => m.SourceColumn.Trim().Length > 0 && m.TargetName.Length > 0).ToList();
        if (_keyColumns.Count == 0)
            throw new ArgumentException("キーにする列名が指定されていません");
    }

    public static bool IsExcelFile(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        && !Path.GetFileName(path).StartsWith("~$"); // エクセルで開いている間にできる一時ファイル

    /// <summary>1ファイルを読んで list に足す</summary>
    public void ReadFile(string file, string displayName, List<SourceRecord> list)
    {
        ForEachSheet(file, reader =>
        {
            var header = FindHeader(reader);
            if (header == null) return;
            _sheetsWithKey++;

            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in _mappings)
            {
                var index = header.Names.FindIndex(h => string.Equals(h, m.SourceColumn.Trim(), StringComparison.OrdinalIgnoreCase));
                if (index < 0) continue;
                // 同じ表の列に複数の元の列を割り当てているときは、先に見つかったものを使う
                columnIndex.TryAdd(m.TargetName, index);
                _foundColumns.Add(m.SourceColumn.Trim());
            }

            var origin = $"{displayName} / {reader.Name}";
            while (reader.Read())
            {
                if (header.KeyIndex >= reader.FieldCount) continue;
                var key = CellText(reader.GetValue(header.KeyIndex));
                if (key.Length == 0) continue; // キーが空の行は読み飛ばす

                var values = new Dictionary<string, string>();
                foreach (var (name, index) in columnIndex)
                    values[name] = index < reader.FieldCount ? CellText(reader.GetValue(index)) : "";
                list.Add(new SourceRecord(key, origin, values));
            }
        });
    }

    /// <summary>キー列が見つかった見出し行の、キー列以外の列名（最初に見つかったシートのもの）</summary>
    public IReadOnlyList<string> ReadHeaders(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            List<string>? names = null;
            try
            {
                ForEachSheet(file, reader =>
                {
                    if (names != null) return;
                    var header = FindHeader(reader);
                    if (header == null) return;
                    names = header.Names.Where((n, i) => i != header.KeyIndex && n.Length > 0)
                                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                });
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn($"エクセルを読めませんでした: {file} ({ex.Message})");
            }
            if (names != null) return names;
        }
        return [];
    }

    /// <summary>読み終わったあとに、見つからなかったものを警告する</summary>
    public void WarnIfNothingFound(string location)
    {
        var keys = string.Join("・", _keyColumns.Select(k => $"「{k}」"));
        if (_sheetsWithKey == 0)
            AppLogger.Instance.Warn($"キー列{keys}が見つかるシートがありませんでした: {location}");
        else
        {
            // どのシートの見出しにも無かった列（列名の間違いなど）
            var missing = _mappings.Select(m => m.SourceColumn.Trim()).Where(c => !_foundColumns.Contains(c)).Distinct().ToList();
            if (missing.Count > 0)
                AppLogger.Instance.Warn($"どのシートの見出しにも無い列がありました（空欄になります）: {string.Join(", ", missing)} ({location})");
        }
    }

    private void ForEachSheet(string file, Action<IExcelDataReader> action)
    {
        // エクセルで開いたままでも読めるよう、共有モードで開く
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            if (_settings.SheetName.Trim().Length > 0
                && !string.Equals(reader.Name, _settings.SheetName.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            action(reader);
        } while (reader.NextResult());
    }

    private record Header(int KeyIndex, List<string> Names);

    /// <summary>上から見出し行を探す。見つかったら reader はその行まで進んでいる</summary>
    private Header? FindHeader(IExcelDataReader reader)
    {
        for (int row = 0; row < HeaderSearchRows && reader.Read(); row++)
        {
            var names = Enumerable.Range(0, reader.FieldCount).Select(i => CellText(reader.GetValue(i))).ToList();
            foreach (var key in _keyColumns) // 候補の順番が先のものを優先
            {
                var index = names.FindIndex(h => string.Equals(h, key, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) return new Header(index, names);
            }
        }
        return null;
    }

    internal static string CellText(object? value) => value switch
    {
        null => "",
        // 1.0 のような数値は "1" にする
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(),
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy/MM/dd") : dt.ToString("yyyy/MM/dd HH:mm:ss"),
        _ => Convert.ToString(value)?.Trim() ?? "",
    };
}
