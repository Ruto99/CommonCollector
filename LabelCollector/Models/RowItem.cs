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

    /// <summary>
    /// どのデータ元（SourceSettings.Id）のどのファイルから読んだか。シートの画面で、フォルダ・ファイルごとのタブに分けるのに使う。
    /// 消えた行は、最後に見つかった場所のタブに残る
    /// </summary>
    [XmlArray("Places")]
    [XmlArrayItem("Place")]
    public List<RowPlace> Places { get; set; } = new();

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

    /// <summary>
    /// 画面用：手入力の列のセル（どの手入力の列も複数の値を入れられる）。ManualList["部品"] は「A / B / C（3件）」のような文字になる。
    /// 値が1つ以下なら、セルに直接入力して書き換えられる。値の一覧は GetManualValues / SetManualValues で読み書きする
    /// </summary>
    [XmlIgnore]
    public MultiValueAccessor ManualList => _manualList ??= new MultiValueAccessor(GetManualValues, SetManualValues);
    private MultiValueAccessor? _manualList;

    /// <summary>
    /// 手入力の列の値の一覧。値ごとに &lt;Cell column="列名"&gt; を1つずつ持つ。
    /// 以前の1つの値も、そのまま1つ目の値として読める
    /// </summary>
    public IReadOnlyList<string> GetManualValues(string column) =>
        ManualCells.Where(c => c.Column == column && c.Value.Length > 0).Select(c => c.Value).ToList();

    /// <summary>手入力の列の値の一覧を入れ替える。変わったら true</summary>
    public bool SetManualValues(string column, IReadOnlyList<string> values)
    {
        if (GetManualValues(column).SequenceEqual(values)) return false;

        // 元の位置（最初の値があったところ）に入れ直す。無ければ末尾
        var at = ManualCells.FindIndex(c => c.Column == column);
        ManualCells.RemoveAll(c => c.Column == column);
        if (at < 0 || at > ManualCells.Count) at = ManualCells.Count;
        ManualCells.InsertRange(at, values.Select(v => new CellValue(column, v)));

        Manual.NotifyAllChanged();
        ManualList.NotifyAllChanged();
        return true;
    }

    // ---- 備考：画面で入力する、自動でも手入力の列でもない値。複数の値を入れられる ----

    /// <summary>備考の値の一覧（保存用）。&lt;Notes&gt;&lt;Note&gt;値&lt;/Note&gt;…&lt;/Notes&gt; で保存する</summary>
    [XmlArray("Notes")]
    [XmlArrayItem("Note")]
    public List<string> Notes { get; set; } = new();

    /// <summary>
    /// 画面用：備考のセル。値が無ければ ""、1つならその値、2つ以上なら「A / B（2件）」。
    /// 値が1つ以下なら、セルに直接入力して書き換えられる（2つ以上あるときは値の一覧で編集する）
    /// </summary>
    [XmlIgnore]
    public string NoteText
    {
        get => MultiValueAccessor.Format(Notes);
        set
        {
            if (Notes.Count > 1) return;
            var v = (value ?? "").Trim();
            SetNotes(v.Length > 0 ? [v] : []);
        }
    }

    /// <summary>備考の値の一覧を入れ替える（空の値は除く）。変わったら true</summary>
    public bool SetNotes(IReadOnlyList<string> values)
    {
        var list = values.Where(v => v.Length > 0).ToList();
        if (Notes.SequenceEqual(list)) return false;
        Notes = list;
        OnPropertyChanged(nameof(NoteText));
        return true;
    }

    /// <summary>以前の1つだけの備考（&lt;Note&gt;値&lt;/Note&gt;）。読み込んだら備考の1つ目の値にする。保存はしない</summary>
    [XmlElement("Note")]
    public string? LegacyNote
    {
        get => null;
        set { if (!string.IsNullOrEmpty(value) && !Notes.Contains(value)) Notes.Insert(0, value); }
    }

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

    /// <summary>
    /// 同じキーの行（元に戻す・やり直しの履歴から読んだもの）の内容を、この行に写す。
    /// 行を作り直さないので、表のスクロール位置などはそのまま
    /// </summary>
    public void CopyFrom(RowItem other)
    {
        State = other.State;
        Origin = other.Origin;
        LastSeen = other.LastSeen;
        Places = other.Places;
        if (!SameCells(AutoCells, other.AutoCells))
        {
            AutoCells = other.AutoCells;
            Auto.NotifyAllChanged();
        }
        if (!SameCells(ManualCells, other.ManualCells))
        {
            ManualCells = other.ManualCells;
            Manual.NotifyAllChanged();
            ManualList.NotifyAllChanged();
        }
        SetNotes(other.Notes);
    }

    private static bool SameCells(List<CellValue> a, List<CellValue> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Column == p.Second.Column && p.First.Value == p.Second.Value);

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

/// <summary>行を読んだ場所：データ元の ID と、読んだファイル（フォルダのデータ元ならフォルダからの相対パス）</summary>
public record RowPlace
{
    public RowPlace() { }
    public RowPlace(string sourceId, string file)
    {
        SourceId = sourceId;
        File = file;
    }

    [XmlAttribute("source")]
    public string SourceId { get; set; } = "";

    [XmlAttribute("file")]
    public string File { get; set; } = "";
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

/// <summary>
/// 手入力の列（複数の値を入れられる）を、表のセルで読み書きする窓口。
/// 画面からは "ManualList[部品]" のように列名でバインドする
/// </summary>
public class MultiValueAccessor(Func<string, IReadOnlyList<string>> values,
                                Func<string, IReadOnlyList<string>, bool> setValues) : INotifyPropertyChanged
{
    /// <summary>
    /// 読み：値が無ければ ""、1つならその値、2つ以上なら「A / B / C（3件）」。
    /// 書き：値が1つ以下のときだけ（セルに直接入力したとき）。空なら値なし。
    /// 2つ以上あるときはセルでは編集させず、値の一覧で編集する
    /// </summary>
    public string this[string column]
    {
        get => Format(values(column));
        set
        {
            if (values(column).Count > 1) return;
            var v = (value ?? "").Trim();
            setValues(column, v.Length > 0 ? [v] : []);
        }
    }

    public static string Format(IReadOnlyList<string> list) => list.Count switch
    {
        0 => "",
        1 => list[0],
        _ => $"{string.Join(" / ", list)}（{list.Count}件）",
    };

    public void NotifyAllChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public event PropertyChangedEventHandler? PropertyChanged;
}
