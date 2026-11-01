using System.ComponentModel;
using System.Xml.Serialization;

namespace LabelCollector.Models;

/// <summary>行の状態</summary>
public enum RowState
{
    /// <summary>データ元に存在する</summary>
    Normal,
    /// <summary>今回の更新で新しく追加された</summary>
    New,
    /// <summary>データ元から消えた（手入力の値は保持している）</summary>
    Missing,
}

/// <summary>
/// 表の1行。Key で行を識別するので、データ元の行が増減しても、手入力の値は Key に結びついたまま残る。
/// 列は自由に増やせるよう「列名→値」で持つ。
/// ・Values（自動）：データ元から読んだ値。更新のたびに入れ替わる
/// ・Manual（手入力）：画面で入力した値。更新しても消えない
/// </summary>
public class RowItem : ObservableObject
{
    private string _key = "";
    private string _origin = "";
    private RowState _state = RowState.New;
    private DateTime _lastSeen;
    private CellAccessor? _auto;
    private CellAccessor? _manual;

    /// <summary>行を識別する一意キー（エクセルのキー列の値、ファイルの相対パスなど）</summary>
    [XmlAttribute("key")]
    public string Key { get => _key; set => Set(ref _key, value); }

    [XmlAttribute("state")]
    public RowState State { get => _state; set => Set(ref _state, value); }

    /// <summary>どこから読んだか（ファイル名・シート名など）</summary>
    public string Origin { get => _origin; set => Set(ref _origin, value); }

    /// <summary>最後にデータ元で見つかった日時</summary>
    public DateTime LastSeen { get => _lastSeen; set => Set(ref _lastSeen, value); }

    /// <summary>データ元から読んだ値（保存用）</summary>
    [XmlArray("Values")]
    [XmlArrayItem("Cell")]
    public List<CellValue> AutoCells { get; set; } = new();

    /// <summary>手入力の値（保存用）</summary>
    [XmlArray("Manual")]
    [XmlArrayItem("Cell")]
    public List<CellValue> ManualCells { get; set; } = new();

    /// <summary>画面用：自動の列の値。Auto["品名"] のように列名で読む</summary>
    [XmlIgnore]
    public CellAccessor Auto => _auto ??= new CellAccessor(() => AutoCells);

    /// <summary>画面用：手入力の列の値。Manual["メモ"] のように列名で読み書きする</summary>
    [XmlIgnore]
    public CellAccessor Manual => _manual ??= new CellAccessor(() => ManualCells);

    /// <summary>自動の列の値をまとめて入れ替える。変わったら true</summary>
    public bool SetAutoValues(IReadOnlyDictionary<string, string> values)
    {
        var current = AutoCells.ToDictionary(c => c.Column, c => c.Value);
        var changed = current.Count != values.Count
                      || values.Any(kv => !current.TryGetValue(kv.Key, out var v) || v != kv.Value);
        if (!changed) return false;

        AutoCells = values.Select(kv => new CellValue(kv.Key, kv.Value)).ToList();
        Auto.NotifyAllChanged();
        return true;
    }

    // ---- 以前の形式（A列・B列の固定2列）を読むためのもの。保存はしない ----

    /// <summary>以前の A列。読み込んだら Origin に入れる</summary>
    [XmlElement("ColumnA")]
    public string? LegacyColumnA
    {
        get => null;
        set { if (!string.IsNullOrEmpty(value) && Origin.Length == 0) Origin = value; }
    }

    /// <summary>以前の B列（手入力）。読み込んだら手入力の列「B列」に入れる</summary>
    [XmlElement("ColumnB")]
    public string? LegacyColumnB
    {
        get => null;
        set { if (!string.IsNullOrEmpty(value)) Manual[LegacyManualColumn] = value; }
    }

    /// <summary>以前の B列を移す、手入力の列の名前</summary>
    public const string LegacyManualColumn = "B列";
}

/// <summary>1つのセルの値（保存用）</summary>
public class CellValue
{
    public CellValue() { }
    public CellValue(string column, string value)
    {
        Column = column;
        Value = value;
    }

    [XmlAttribute("column")]
    public string Column { get; set; } = "";

    [XmlText]
    public string Value { get; set; } = "";
}

/// <summary>
/// 列名で値を読み書きするための窓口。画面（DataGrid）からは "Auto[品名]" のように列名でバインドする
/// </summary>
public class CellAccessor(Func<List<CellValue>> cells) : INotifyPropertyChanged
{
    public string this[string column]
    {
        get => cells().FirstOrDefault(c => c.Column == column)?.Value ?? "";
        set
        {
            var list = cells();
            var cell = list.FirstOrDefault(c => c.Column == column);
            if (cell == null)
            {
                if (string.IsNullOrEmpty(value)) return;
                list.Add(new CellValue(column, value));
            }
            else
            {
                if (cell.Value == value) return;
                cell.Value = value ?? "";
            }
            NotifyAllChanged();
        }
    }

    /// <summary>値が変わったことを画面に知らせる（列名で指定しているバインドすべて）</summary>
    public void NotifyAllChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public event PropertyChangedEventHandler? PropertyChanged;
}
