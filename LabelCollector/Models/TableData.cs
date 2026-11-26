using System.Collections.ObjectModel;
using System.Xml.Serialization;
using LabelCollector.Models.Sources;

namespace LabelCollector.Models;

/// <summary>中間データ（.lc ファイル、中身は XML）のルート。複数のシートを持つ</summary>
[XmlRoot("Workspace")]
public class TableData
{
    /// <summary>今の保存形式の番号</summary>
    public const int CurrentVersion = 4;

    [XmlAttribute("version")]
    public int Version { get; set; } = CurrentVersion;

    public DateTime UpdatedAt { get; set; }

    [XmlArray("Sheets")]
    [XmlArrayItem("Sheet")]
    public List<SheetData> Sheets { get; set; } = new();

    /// <summary>以前の形式で保存されたシートを、今の形式に直す（読み込み直後に呼ぶ）</summary>
    public void MigrateLegacy()
    {
        foreach (var sheet in Sheets) sheet.MigrateLegacy();
        Version = CurrentVersion;
    }
}

/// <summary>
/// 1つのシート（表）。データ元の一覧と、手入力の列と、表の内容を持つ。
/// 表の列は「データ元から取り込む列（自動）」と「手入力の列」でできている
/// </summary>
public class SheetData
{
    /// <summary>
    /// どのシートにも必ずある手入力の列（この順で先頭に並ぶ）。プロパティで消したり名前を変えたりはできない
    /// </summary>
    public static readonly IReadOnlyList<string> FixedManualColumns = ["A列", RowItem.LegacyManualColumn];

    [XmlAttribute("name")]
    public string Name { get; set; } = "";

    /// <summary>手入力の列の名前</summary>
    [XmlArray("ManualColumns")]
    [XmlArrayItem("Column")]
    public ObservableCollection<TextItem> ManualColumns { get; set; } = new();

    /// <summary>
    /// データ元の一覧。種類（エクセルファイル・エクセルフォルダなど）が混ざっていてよい。
    /// 保存時は xsi:type="ExcelFolder" のように種類の名前が付く
    /// </summary>
    [XmlArray("Sources")]
    [XmlArrayItem("Source")]
    public ObservableCollection<SourceSettings> Sources { get; set; } = new();

    [XmlArray("Rows")]
    [XmlArrayItem("Row")]
    public List<RowItem> Rows { get; set; } = new();

    /// <summary>新しいシートを作る（手入力の列「A列」「B列」付き）</summary>
    public static SheetData CreateNew(string name)
    {
        var sheet = new SheetData { Name = name };
        sheet.EnsureFixedManualColumns();
        return sheet;
    }

    /// <summary>
    /// 固定の手入力の列（A列・B列）が先頭に並ぶようにし、消せない印（IsFixed）を付ける。
    /// 無ければ足し、同じ名前の列が別の位置にあれば先頭に移す（入力済みの値は列名に結びついているのでそのまま出る）
    /// </summary>
    public void EnsureFixedManualColumns()
    {
        foreach (var c in ManualColumns) c.IsFixed = false;
        for (int i = 0; i < FixedManualColumns.Count; i++)
        {
            var name = FixedManualColumns[i];
            var item = ManualColumns.FirstOrDefault(c => c.Value.Trim() == name);
            if (item == null)
            {
                item = new TextItem(name);
                ManualColumns.Insert(i, item);
            }
            else
            {
                var at = ManualColumns.IndexOf(item);
                if (at != i) ManualColumns.Move(at, i);
                item.Value = name; // 前後の空白を取る
            }
            item.IsFixed = true;
        }
    }

    // ---- 以前の形式（種類ごとに決まった設定を持つシート）を読むためのもの。保存はしない ----

    [XmlAttribute("type")]
    public string? LegacyType { get; set; }
    public bool ShouldSerializeLegacyType() => false;

    [XmlElement("FolderPath")]
    public string? LegacyFolderPath { get; set; }
    public bool ShouldSerializeLegacyFolderPath() => false;

    [XmlElement("FilePattern")]
    public string? LegacyFilePattern { get; set; }
    public bool ShouldSerializeLegacyFilePattern() => false;

    [XmlArray("ExcelSources")]
    [XmlArrayItem("ExcelSource")]
    public List<LegacyExcelSource> LegacyExcelSources { get; set; } = new();
    public bool ShouldSerializeLegacyExcelSources() => false;

    /// <summary>以前の形式なら、今の形式（データ元の一覧＋手入力の列）に直す</summary>
    internal void MigrateLegacy()
    {
        EnsureFixedManualColumns(); // 以前の .lc のシートにも A列・B列を足す
        if (LegacyType == null) return;

        if (LegacyType == "Excel")
        {
            foreach (var old in LegacyExcelSources)
            {
                var src = new ExcelFolderSourceSettings { FolderPath = old.FolderPath, IncludeSubfolders = true };
                foreach (var k in old.KeyColumns) src.KeyColumns.Add(new TextItem(k));
                Sources.Add(src);
            }
        }
        else if (!string.IsNullOrWhiteSpace(LegacyFolderPath))
        {
            // フォルダ（ファイル一覧）のシート。以前の A列はファイル名だった
            Sources.Add(new FileListSourceSettings
            {
                FolderPath = LegacyFolderPath,
                FilePattern = string.IsNullOrWhiteSpace(LegacyFilePattern) ? "*.*" : LegacyFilePattern,
                Columns = { new ColumnMapping("ファイル名") },
            });
        }
        // それ以外（以前の DB のシートなど）はデータ元なし。手入力の値は残る

        // 以前の B列は手入力の列「B列」に移っている（B列は固定の手入力の列なので、上で足してある）

        LegacyType = null;
        LegacyFolderPath = null;
        LegacyFilePattern = null;
        LegacyExcelSources.Clear();
    }
}

/// <summary>以前の形式のエクセル指定（読み込み専用）</summary>
public class LegacyExcelSource
{
    public string FolderPath { get; set; } = "";

    [XmlArray("KeyColumns")]
    [XmlArrayItem("Column")]
    public List<string> KeyColumns { get; set; } = new();

    [XmlElement("KeyColumn")]
    public string? KeyColumn
    {
        get => null;
        set { if (!string.IsNullOrWhiteSpace(value) && !KeyColumns.Contains(value)) KeyColumns.Add(value); }
    }
}
