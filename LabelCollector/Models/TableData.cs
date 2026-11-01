using System.Xml.Serialization;

namespace LabelCollector.Models;

/// <summary>中間データ（.lc ファイル、中身は XML）のルート。複数のシートを持つ</summary>
[XmlRoot("Workspace")]
public class TableData
{
    [XmlAttribute("version")]
    public int Version { get; set; } = 3;

    public DateTime UpdatedAt { get; set; }

    [XmlArray("Sheets")]
    [XmlArrayItem("Sheet")]
    public List<SheetData> Sheets { get; set; } = new();
}

public enum SourceType
{
    /// <summary>フォルダ内のファイル一覧</summary>
    Folder,
    /// <summary>フォルダ内のエクセルファイルの行</summary>
    Excel,
}

/// <summary>1つのデータ元（フォルダまたはエクセル）と、その表の内容</summary>
public class SheetData
{
    [XmlAttribute("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 種類。古いXMLに残っている未対応の種類（Sqlite など）でも読み込みに失敗しないよう、文字列で保存する
    /// </summary>
    [XmlAttribute("type")]
    public string TypeText { get; set; } = nameof(SourceType.Folder);

    [XmlIgnore]
    public SourceType Type
    {
        get => Enum.TryParse<SourceType>(TypeText, out var t) ? t : SourceType.Folder;
        set => TypeText = value.ToString();
    }

    /// <summary>XMLの種類が未対応のもの（旧バージョンのDBシートなど）か</summary>
    [XmlIgnore]
    public bool IsUnknownType => !Enum.TryParse<SourceType>(TypeText, out _);

    public string FolderPath { get; set; } = "";
    public string FilePattern { get; set; } = "*.*";

    /// <summary>エクセルの読み込み設定（プロパティウィンドウで追加する）</summary>
    [XmlArray("ExcelSources")]
    [XmlArrayItem("ExcelSource")]
    public List<ExcelSourceData> ExcelSources { get; set; } = new();

    [XmlArray("Rows")]
    [XmlArrayItem("Row")]
    public List<RowItem> Rows { get; set; } = new();
}

/// <summary>エクセルの読み込み設定 1件分：どのフォルダの、どの列をキーにするか</summary>
public class ExcelSourceData
{
    /// <summary>キー列名の既定値</summary>
    public const string DefaultKeyColumn = "ID";

    /// <summary>エクセルファイルが入っているフォルダ</summary>
    public string FolderPath { get; set; } = "";

    /// <summary>
    /// キーにする列の名前（見出し行の文字）の候補。ファイルによって列名が違うことがあるので複数指定できる。
    /// 各シートでは、この順に探して最初に見つかった列をキーにする
    /// </summary>
    [XmlArray("KeyColumns")]
    [XmlArrayItem("Column")]
    public List<string> KeyColumns { get; set; } = new();

    /// <summary>
    /// 以前の形式（キー列名が1つだけの &lt;KeyColumn&gt;）を読むためのもの。読んだ値は KeyColumns に入る。保存はしない
    /// </summary>
    [XmlElement("KeyColumn")]
    public string? LegacyKeyColumn
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && !KeyColumns.Contains(value)) KeyColumns.Add(value);
        }
    }

    /// <summary>キー列名「ID」が入った新しい設定を作る</summary>
    public static ExcelSourceData CreateDefault(string folderPath = "") =>
        new() { FolderPath = folderPath, KeyColumns = { DefaultKeyColumn } };
}

/// <summary>データ元から取ってきた1件分</summary>
public record SourceRecord(string Key, string ValueA);
