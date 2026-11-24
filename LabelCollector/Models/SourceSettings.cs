using System.Collections.ObjectModel;
using System.Xml.Serialization;

namespace LabelCollector.Models;

/// <summary>
/// データ元（エクセルファイル、エクセルフォルダなど）の設定の基底クラス。
/// 新しい種類のデータ元を作るときは、このクラス（キー列で行を読む表形式なら TableSourceSettings）を継承し、
/// [XmlType] で保存用の名前を付けて、DataSourceRegistry に登録する。
/// 詳しくは docs/データ元の追加方法.md を参照。
/// </summary>
public abstract class SourceSettings : ObservableObject
{
    /// <summary>
    /// シートの中でデータ元を見分けるための ID。行がどのデータ元から読まれたか（RowItem.Places）に使う。
    /// 以前の .lc には無いので、読み込み時に新しく作られ、次の保存で書き込まれる
    /// </summary>
    [XmlAttribute("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// データ元から表に取り込む列。「元の列名」→「表の列名」の対応。
    /// 複数のデータ元が同じ「表の列名」に取り込むと、同じキーの行の同じ列にまとまる
    /// </summary>
    [XmlArray("Columns")]
    [XmlArrayItem("Column")]
    public ObservableCollection<ColumnMapping> Columns { get; set; } = new();

    /// <summary>ログなどに出す、このデータ元の場所（パスなど）</summary>
    [XmlIgnore]
    public abstract string Location { get; }
}

/// <summary>
/// 見出し行のある表を読むデータ元（エクセルなど）の設定。キー列名で行を識別する
/// </summary>
public abstract class TableSourceSettings : SourceSettings
{
    private string _sheetName = "";
    private int _headerRow;
    private int _headerRowCount = 1;
    private bool _fillMergedCells = true;

    /// <summary>
    /// キーにする列の名前（見出し行の文字）の候補。ファイルによって列名が違うことがあるので複数指定できる。
    /// 各シートでは、この順に探して最初に見つかった列をキーにする
    /// </summary>
    [XmlArray("KeyColumns")]
    [XmlArrayItem("Column")]
    public ObservableCollection<TextItem> KeyColumns { get; set; } = new();

    /// <summary>読むシートの名前。空なら全シート（キー列が見つかるシートだけ読む）</summary>
    public string SheetName { get => _sheetName; set => Set(ref _sheetName, value); }

    /// <summary>
    /// 見出しの行（1始まり）。0 なら自動：上から20行以内で、キー列名が見つかる行を探す
    /// </summary>
    public int HeaderRow { get => _headerRow; set => Set(ref _headerRow, Math.Max(0, value)); }

    /// <summary>
    /// 見出しの行数。2 以上なら、上の行を大項目として「大項目/列名」の形の列名にする（例：数量/入庫）。
    /// 下の段の名前だけ（例：入庫）でも、ほかの列と重ならなければ指定できる
    /// </summary>
    public int HeaderRowCount { get => _headerRowCount; set => Set(ref _headerRowCount, Math.Clamp(value, 1, 10)); }

    /// <summary>
    /// データ行の結合セルの値を、結合範囲のすべてのセルに入れるか（縦に結合された値を下の行にも入れる）。
    /// 見出しの結合セルは、この設定によらず常に埋める
    /// </summary>
    public bool FillMergedCells { get => _fillMergedCells; set => Set(ref _fillMergedCells, value); }

    /// <summary>空欄を除いたキー列名の候補</summary>
    public List<string> GetKeyColumnNames() =>
        KeyColumns.Select(k => k.Value.Trim()).Where(k => k.Length > 0)
                  .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>フォルダを指定するデータ元（プロパティで「…」ボタンからフォルダを選べる）</summary>
public interface IFolderLocation
{
    string FolderPath { get; set; }
}

/// <summary>ファイルを指定するデータ元（プロパティで「…」ボタンからファイルを選べる）</summary>
public interface IFileLocation
{
    string FilePath { get; set; }

    /// <summary>ファイルを選ぶダイアログのフィルター（例："エクセル (*.xlsx)|*.xlsx"）</summary>
    string FileDialogFilter { get; }
}

/// <summary>画面で編集できる文字列1つ（キー列名・手入力の列名など）。XML では &lt;Column&gt;ID&lt;/Column&gt; のように保存</summary>
public class TextItem : ObservableObject
{
    private string _value = "";

    public TextItem() { }
    public TextItem(string value) => _value = value;

    [XmlText]
    public string Value { get => _value; set => Set(ref _value, value); }
}

/// <summary>
/// 手入力の列。XML では &lt;Column&gt;部品&lt;/Column&gt; のように保存。
/// 複数の値を入れられないようにした列だけ &lt;Column multiple="false"&gt; になる
/// </summary>
public class ManualColumn : TextItem
{
    private bool _allowMultiple = true;

    public ManualColumn() { }
    public ManualColumn(string value) : base(value) { }

    /// <summary>
    /// 1つのキーに複数の値を入れられる列（既定で入れられる。以前の .lc の列も同じ）。
    /// 値が1つまではセルに直接入力し、2つ目からはセルの下に開く一覧で入力する
    /// （区切りの文字を打たないので、区切りの間違いが起きない）
    /// </summary>
    [XmlAttribute("multiple")]
    public bool AllowMultiple { get => _allowMultiple; set => Set(ref _allowMultiple, value); }
    public bool ShouldSerializeAllowMultiple() => !AllowMultiple;
}

/// <summary>データ元の列を、表のどの列に取り込むか</summary>
public class ColumnMapping : ObservableObject
{
    private string _sourceColumn = "";
    private string _columnName = "";

    public ColumnMapping() { }
    public ColumnMapping(string sourceColumn, string columnName = "")
    {
        _sourceColumn = sourceColumn;
        _columnName = columnName;
    }

    /// <summary>データ元での列名（エクセルなら見出しの文字）</summary>
    [XmlAttribute("source")]
    public string SourceColumn { get => _sourceColumn; set => Set(ref _sourceColumn, value); }

    /// <summary>表での列名。空なら元の列名と同じ</summary>
    [XmlAttribute("name")]
    public string ColumnName { get => _columnName; set => Set(ref _columnName, value); }

    /// <summary>実際に使う表の列名</summary>
    [XmlIgnore]
    public string TargetName => string.IsNullOrWhiteSpace(ColumnName) ? SourceColumn.Trim() : ColumnName.Trim();
}

/// <summary>
/// データ元から読んだ1行分。
/// Key＝行を識別するキー、Origin＝どこから読んだか（ファイル名・シート名など）、Values＝表の列名→値、
/// File＝読んだファイル（フォルダのデータ元ならフォルダからの相対パス、ファイルのデータ元ならファイル名。ファイルを持たないデータ元は空）
/// </summary>
public record SourceRecord(string Key, string Origin, IReadOnlyDictionary<string, string> Values, string File = "")
{
    /// <summary>まとめた後の行で、どのデータ元のどのファイルから読んだか（RecordCombiner が入れる）</summary>
    public IReadOnlyList<RowPlace> Places { get; init; } = [];
}
