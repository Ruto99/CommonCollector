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
/// エクセルの表を読む共通処理。シートごとに次の順で読む。
/// 1. シート全体を読み込み、結合セルの値を結合範囲に埋める（見出しは常に、データ行は設定により）
/// 2. 見出しの行を決める（指定された行、または上から20行以内でキー列名が見つかる行）
/// 3. 見出しの行数が2以上なら、上の段から順に「大項目/列名」とつなげて列名にする（例：数量/入庫）
/// 4. 見出しの下の行を、キー列が空でなければ1行として読む
/// 列名は「大項目/列名」の全体か、ほかと重ならなければ下の段の名前だけ（例：入庫）でも指定できる。
/// キー列名の候補が複数あるときは、候補の順番が先のものをキーにする。
/// </summary>
internal class ExcelTable
{
    private static readonly string[] Extensions = [".xlsx", ".xlsm", ".xls"];

    /// <summary>見出しの行を自動で探す範囲（上から何行目まで）</summary>
    private const int HeaderSearchRows = 20;

    /// <summary>2段以上の見出しをつなぐ文字</summary>
    public const string HeaderSeparator = "/";

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
        foreach (var sheet in ReadSheets(file))
        {
            var header = FindHeader(sheet);
            if (header == null) continue;
            _sheetsWithKey++;

            // 取り込む列の位置（同じ表の列に複数の元の列を割り当てているときは、先に見つかったものを使う）
            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in _mappings)
            {
                var index = header.Resolve(m.SourceColumn.Trim());
                if (index < 0) continue;
                columnIndex.TryAdd(m.TargetName, index);
                _foundColumns.Add(m.SourceColumn.Trim());
            }

            var origin = $"{displayName} / {sheet.Name}";
            for (int r = header.FirstDataRow; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                var key = Cell(row, header.KeyIndex);
                if (key.Length == 0) continue; // キーが空の行は読み飛ばす

                var values = new Dictionary<string, string>();
                foreach (var (name, index) in columnIndex) values[name] = Cell(row, index);
                list.Add(new SourceRecord(key, origin, values));
            }
        }
    }

    /// <summary>キー列が見つかった見出しの、キー列以外の列名（最初に見つかったシートのもの）</summary>
    public IReadOnlyList<string> ReadHeaders(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                foreach (var sheet in ReadSheets(file))
                {
                    var header = FindHeader(sheet);
                    if (header == null) continue;
                    return header.Names.Where((n, i) => i != header.KeyIndex && n.Length > 0)
                                       .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn($"エクセルを読めませんでした: {file} ({ex.Message})");
            }
        }
        return [];
    }

    /// <summary>読み終わったあとに、見つからなかったものを警告する</summary>
    public void WarnIfNothingFound(string location)
    {
        var keys = string.Join("・", _keyColumns.Select(k => $"「{k}」"));
        if (_sheetsWithKey == 0)
        {
            var where = _settings.HeaderRow > 0 ? $"{_settings.HeaderRow}行目の見出し" : "見出し";
            AppLogger.Instance.Warn($"{where}にキー列{keys}が見つかるシートがありませんでした: {location}");
            return;
        }

        // どのシートの見出しにも無かった列（列名の間違いや、下の段の名前が重なっている場合など）
        var missing = _mappings.Select(m => m.SourceColumn.Trim()).Where(c => !_foundColumns.Contains(c)).Distinct().ToList();
        if (missing.Count > 0)
            AppLogger.Instance.Warn($"どのシートの見出しにも無い列がありました（空欄になります）: {string.Join(", ", missing)} ({location})"
                                    + (_settings.HeaderRowCount > 1 ? $"　※2段以上の見出しは「大項目{HeaderSeparator}列名」で指定できます" : ""));
    }

    // ---- シートの読み込み ----

    /// <summary>シート1枚分の内容（セルの文字）</summary>
    private sealed record Sheet(string Name, List<string[]> Rows)
    {
        /// <summary>結合セルの範囲（0始まり）</summary>
        public List<Merge> Merges { get; init; } = new();

        /// <summary>見出し用：結合範囲の中なら左上のセルの値を返す</summary>
        public string MergedCell(int row, int column)
        {
            foreach (var m in Merges)
                if (row >= m.FromRow && row <= m.ToRow && column >= m.FromColumn && column <= m.ToColumn)
                    return m.FromRow < Rows.Count ? Cell(Rows[m.FromRow], m.FromColumn) : "";
            return row < Rows.Count ? Cell(Rows[row], column) : "";
        }
    }

    /// <summary>ファイルの全シートを読む（シート名の指定があればそのシートだけ）。結合セルはここで埋める</summary>
    private List<Sheet> ReadSheets(string file)
    {
        var sheets = new List<Sheet>();

        // エクセルで開いたままでも読めるよう、共有モードで開く
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            var name = reader.Name;
            if (_settings.SheetName.Trim().Length > 0
                && !string.Equals(name, _settings.SheetName.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            var rows = new List<string[]>();
            while (reader.Read())
            {
                var row = new string[reader.FieldCount];
                for (int i = 0; i < row.Length; i++) row[i] = CellText(reader.GetValue(i));
                rows.Add(row);
            }

            // 結合セルの位置は、シートを最後まで読んだあとで分かる（xlsx はシートの末尾に書かれている）
            var merges = reader.MergeCells ?? [];
            sheets.Add(new Sheet(name, rows) { Merges = merges.Select(m => new Merge(m.FromRow, m.FromColumn, m.ToRow, m.ToColumn)).ToList() });
        } while (reader.NextResult());

        return sheets;
    }

    private sealed record Merge(int FromRow, int FromColumn, int ToRow, int ToColumn);

    /// <summary>
    /// データ行の結合範囲に、左上のセルの値を入れる。
    /// firstDataRow より上（見出し）から始まる結合は対象にしない（見出しの文字がデータ行に入らないように）
    /// </summary>
    private static void FillMerges(Sheet sheet, int firstDataRow)
    {
        foreach (var m in sheet.Merges)
        {
            if (m.FromRow < firstDataRow || m.FromRow >= sheet.Rows.Count) continue;
            var value = Cell(sheet.Rows[m.FromRow], m.FromColumn);
            if (value.Length == 0) continue;
            for (int r = m.FromRow; r <= m.ToRow && r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                if (row.Length <= m.ToColumn)
                {
                    Array.Resize(ref row, m.ToColumn + 1);
                    for (int c = 0; c < row.Length; c++) row[c] ??= "";
                    sheet.Rows[r] = row;
                }
                for (int c = m.FromColumn; c <= m.ToColumn; c++)
                    if (row[c].Length == 0) row[c] = value;
            }
        }
    }

    // ---- 見出し ----

    /// <summary>見出し：列ごとの名前と、キー列の位置、データが始まる行</summary>
    private sealed class Header(List<string> names, int firstDataRow)
    {
        /// <summary>列ごとの名前（2段以上なら「大項目/列名」）</summary>
        public List<string> Names { get; } = names;
        public int FirstDataRow { get; } = firstDataRow;
        public int KeyIndex { get; set; } = -1;

        /// <summary>
        /// 列名から列の位置を探す。名前全体が一致する列、なければ下の段の名前が一致する列（1つだけのとき）。
        /// 見つからなければ -1
        /// </summary>
        public int Resolve(string name)
        {
            var full = Names.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (full >= 0) return full;

            var byLast = Names.Select((n, i) => (Last: n.Split(HeaderSeparator).Last(), Index: i))
                              .Where(x => string.Equals(x.Last, name, StringComparison.OrdinalIgnoreCase))
                              .ToList();
            return byLast.Count == 1 ? byLast[0].Index : -1;
        }
    }

    /// <summary>見出しを決める。キー列が見つからなければ null</summary>
    private Header? FindHeader(Sheet sheet)
    {
        var count = Math.Max(1, _settings.HeaderRowCount);

        Header? header = null;
        if (_settings.HeaderRow > 0)
        {
            header = TryHeaderAt(sheet, _settings.HeaderRow - 1, count);
        }
        else
        {
            // 自動：キー列名が見つかる最初の行を、見出しの一番下の段とみなす。
            // キー列が縦に結合されている（例：A1:A2 が「ID」）ときは、結合の一番下の行を一番下の段とする
            for (int r = 0; r < HeaderSearchRows && r < sheet.Rows.Count; r++)
            {
                if (TryHeaderAt(sheet, r, 1) == null) continue;
                var bottom = r;
                while (bottom + 1 < r + count && TryHeaderAt(sheet, bottom + 1, 1) != null) bottom++;
                header = TryHeaderAt(sheet, Math.Max(0, bottom - count + 1), count);
                break;
            }
        }
        if (header == null) return null;

        // データ行の結合セル（縦に結合された値など）
        if (_settings.FillMergedCells) FillMerges(sheet, header.FirstDataRow);
        return header;
    }

    /// <summary>startRow から count 行を見出しとみなして、キー列が見つかれば見出しを返す</summary>
    private Header? TryHeaderAt(Sheet sheet, int startRow, int count)
    {
        if (startRow < 0 || startRow >= sheet.Rows.Count) return null;

        var rowIndexes = Enumerable.Range(startRow, count).Where(r => r < sheet.Rows.Count).ToList();
        var width = Math.Max(rowIndexes.Max(r => sheet.Rows[r].Length),
                             sheet.Merges.Where(m => rowIndexes.Contains(m.FromRow)).Select(m => m.ToColumn + 1).DefaultIfEmpty(0).Max());
        var names = new List<string>();
        for (int c = 0; c < width; c++)
        {
            // 上の段から順につなぐ。結合セルは左上の値を使い（大項目が横に結合されていても、下の列ごとに名前が付く）、
            // 空欄と、結合で同じ文字が続く部分は省く（例：ID/ID → ID）
            var parts = new List<string>();
            foreach (var r in rowIndexes)
            {
                var text = sheet.MergedCell(r, c);
                if (text.Length > 0 && (parts.Count == 0 || parts[^1] != text)) parts.Add(text);
            }
            names.Add(string.Join(HeaderSeparator, parts));
        }

        var header = new Header(names, startRow + count);
        foreach (var key in _keyColumns) // 候補の順番が先のものを優先
        {
            var index = header.Resolve(key);
            if (index < 0) continue;
            header.KeyIndex = index;
            return header;
        }
        return null;
    }

    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index] ?? "" : "";

    internal static string CellText(object? value) => value switch
    {
        null => "",
        // 1.0 のような数値は "1" にする
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(),
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy/MM/dd") : dt.ToString("yyyy/MM/dd HH:mm:ss"),
        _ => Convert.ToString(value)?.Trim() ?? "",
    };
}
