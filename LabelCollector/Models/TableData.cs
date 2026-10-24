using System.Xml.Serialization;

namespace LabelCollector.Models;

/// <summary>中間データ（XML）のルート。複数のシートを持つ</summary>
[XmlRoot("Workspace")]
public class TableData
{
    [XmlAttribute("version")]
    public int Version { get; set; } = 2;

    public DateTime UpdatedAt { get; set; }

    [XmlArray("Sheets")]
    [XmlArrayItem("Sheet")]
    public List<SheetData> Sheets { get; set; } = new();
}

public enum SourceType
{
    Folder,
    Sqlite,
}

/// <summary>1つのデータ元（フォルダまたはDB）と、その表の内容</summary>
public class SheetData
{
    [XmlAttribute("name")]
    public string Name { get; set; } = "";

    [XmlAttribute("type")]
    public SourceType Type { get; set; } = SourceType.Folder;

    public string FolderPath { get; set; } = "";
    public string FilePattern { get; set; } = "*.*";
    public string DbPath { get; set; } = "";
    /// <summary>1列目をキー、2列目をA列として読む SQL</summary>
    public string DbQuery { get; set; } = "SELECT id, name FROM items ORDER BY id";

    [XmlArray("Rows")]
    [XmlArrayItem("Row")]
    public List<RowItem> Rows { get; set; } = new();
}

/// <summary>データ元から取ってきた1件分</summary>
public record SourceRecord(string Key, string ValueA);
