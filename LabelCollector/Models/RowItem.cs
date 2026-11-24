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

        NotifyManualChanged();
        return true;
    }

    // ---- 組の列（LinkedColumns：例 部品番号と数量） ----

    /// <summary>
    /// 画面用：組の列のセル。ManualPair["数量"] は、組の順に2つ目の列の値を並べた「10 / －（2件）」のような文字。
    /// 組が1つ以下なら、セルに直接入力してその組の値を書き換えられる
    /// </summary>
    [XmlIgnore]
    public PairAccessor ManualPair => _manualPair ??= new PairAccessor(this);
    private PairAccessor? _manualPair;

    /// <summary>
    /// 組の値の一覧（1つ目の列の値, 2つ目の列の値）。値に組の番号（item）を付けて保存しているので、
    /// 片方が空の組もずれずに持てる。組の番号の無い値（以前の値）は、それぞれの列の順番で組にする
    /// </summary>
    public IReadOnlyList<(string First, string Second)> GetPairs(string first, string second)
    {
        var a = ManualCells.Where(c => c.Column == first).ToList();
        var b = ManualCells.Where(c => c.Column == second).ToList();

        if (a.Any(c => c.Item > 0) || b.Any(c => c.Item > 0))
        {
            return a.Concat(b).Select(c => c.Item).Distinct().Order()
                    .Select(i => (a.FirstOrDefault(c => c.Item == i)?.Value ?? "",
                                  b.FirstOrDefault(c => c.Item == i)?.Value ?? ""))
                    .Where(p => p.Item1.Length > 0 || p.Item2.Length > 0)
                    .ToList();
        }

        var av = a.Select(c => c.Value).Where(v => v.Length > 0).ToList();
        var bv = b.Select(c => c.Value).Where(v => v.Length > 0).ToList();
        return Enumerable.Range(0, Math.Max(av.Count, bv.Count))
                         .Select(i => (i < av.Count ? av[i] : "", i < bv.Count ? bv[i] : ""))
                         .ToList();
    }

    /// <summary>組の値の一覧を入れ替える（前後の空白を取り、両方とも空の組は除く）。変わったら true</summary>
    public bool SetPairs(string first, string second, IReadOnlyList<(string First, string Second)> pairs)
    {
        var list = pairs.Select(p => (First: p.First.Trim(), Second: p.Second.Trim()))
                        .Where(p => p.First.Length > 0 || p.Second.Length > 0).ToList();
        var current = GetPairs(first, second);
        var unchanged = current.SequenceEqual(list)
                        && ManualCells.Where(c => c.Column == first || c.Column == second).All(c => c.Item > 0);
        if (unchanged) return false;

        // 元の位置（どちらかの列の最初の値があったところ）に入れ直す。無ければ末尾
        var at = ManualCells.FindIndex(c => c.Column == first || c.Column == second);
        ManualCells.RemoveAll(c => c.Column == first || c.Column == second);
        if (at < 0 || at > ManualCells.Count) at = ManualCells.Count;

        var cells = new List<CellValue>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].First.Length > 0) cells.Add(new CellValue(first, list[i].First) { Item = i + 1 });
            if (list[i].Second.Length > 0) cells.Add(new CellValue(second, list[i].Second) { Item = i + 1 });
        }
        ManualCells.InsertRange(at, cells);

        NotifyManualChanged();
        return true;
    }

    /// <summary>手入力の値が変わったことを、手入力の列を表示しているすべてのセルに知らせる</summary>
    private void NotifyManualChanged()
    {
        Manual.NotifyAllChanged();
        ManualList.NotifyAllChanged();
        ManualPair.NotifyAllChanged();
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
            NotifyManualChanged();
        }
    }

    private static bool SameCells(List<CellValue> a, List<CellValue> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Column == p.Second.Column && p.First.Value == p.Second.Value
                                                && p.First.Item == p.Second.Item);

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

    /// <summary>組の番号（1から）。組の列（部品番号と数量など）の値だけに付ける。0 は組なし</summary>
    [XmlAttribute("item")]
    public int Item { get; set; }
    public bool ShouldSerializeItem() => Item > 0;
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

/// <summary>
/// 組の列（LinkedColumns）を、表のセルで読み書きする窓口。画面からは "ManualPair[数量]" のように列名でバインドする。
/// どちらの列のセルも、組の順に自分の列の値を並べて出すので、2つの列の値の並びはいつもそろう
/// </summary>
public class PairAccessor(RowItem row) : INotifyPropertyChanged
{
    private static IReadOnlyList<(string First, string Second)> Pairs(RowItem row) =>
        row.GetPairs(LinkedColumns.First, LinkedColumns.Second);

    private static string Side((string First, string Second) pair, string column) =>
        column == LinkedColumns.First ? pair.First : pair.Second;

    /// <summary>
    /// 読み：組が無ければ ""、1つならその組のこの列の値、2つ以上なら「10 / －（2件）」（空の値は －）。
    /// 書き：組が1つ以下のときだけ（セルに直接入力したとき）。その組のこの列の値を書き換える
    /// </summary>
    public string this[string column]
    {
        get
        {
            var pairs = Pairs(row);
            return pairs.Count switch
            {
                0 => "",
                1 => Side(pairs[0], column),
                _ => $"{string.Join(" / ", pairs.Select(p => Side(p, column) is { Length: > 0 } v ? v : "－"))}（{pairs.Count}件）",
            };
        }
        set
        {
            var pairs = Pairs(row);
            if (pairs.Count > 1) return;
            (string First, string Second) pair = pairs.Count == 1 ? pairs[0] : ("", "");
            var v = (value ?? "").Trim();
            pair = column == LinkedColumns.First ? (v, pair.Second) : (pair.First, v);
            row.SetPairs(LinkedColumns.First, LinkedColumns.Second, [pair]);
        }
    }

    /// <summary>組の数（2つ以上なら、セルでは編集させずに組の一覧を開く）</summary>
    public int Count => Pairs(row).Count;

    /// <summary>この列の値が空の組があるか（組が無いときも true）。表のセルを赤枠にするのに使う</summary>
    public bool HasEmpty(string column)
    {
        var pairs = Pairs(row);
        return pairs.Count == 0 || pairs.Any(p => Side(p, column).Length == 0);
    }

    /// <summary>画面用：HasEmpty を "ManualPair.Empty[数量]" のようにバインドするための窓口</summary>
    public EmptyIndexer Empty => _empty ??= new EmptyIndexer(this);
    private EmptyIndexer? _empty;

    public void NotifyAllChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        _empty?.NotifyAllChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public class EmptyIndexer(PairAccessor owner) : INotifyPropertyChanged
    {
        public bool this[string column] => owner.HasEmpty(column);

        public void NotifyAllChanged() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
