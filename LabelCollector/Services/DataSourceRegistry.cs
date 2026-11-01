using LabelCollector.Models;
using LabelCollector.Models.Sources;
using LabelCollector.Services.Sources;

namespace LabelCollector.Services;

/// <summary>データ元から行を読む処理。データ元の種類ごとに1つ作る</summary>
public interface IDataSourceReader
{
    /// <summary>行を読む。読めないとき（フォルダが無いなど）は例外を投げる</summary>
    List<SourceRecord> Read();

    /// <summary>
    /// 取り込める列の名前（エクセルなら見出しの文字）。プロパティの「列の候補を読み込む」で使う。
    /// キー列は含めない
    /// </summary>
    IReadOnlyList<string> GetColumnNames();
}

/// <summary>データ元の種類 1つ分の情報</summary>
public sealed class DataSourceKind
{
    /// <summary>画面に出す名前（例：エクセルフォルダ）</summary>
    public required string DisplayName { get; init; }

    /// <summary>画面に出すアイコン（絵文字）</summary>
    public required string Icon { get; init; }

    /// <summary>設定のクラス（SourceSettings を継承したもの）</summary>
    public required Type SettingsType { get; init; }

    /// <summary>新しい設定を作る</summary>
    public required Func<SourceSettings> Create { get; init; }

    /// <summary>設定から読み取り処理を作る</summary>
    public required Func<SourceSettings, IDataSourceReader> CreateReader { get; init; }
}

/// <summary>
/// データ元の種類の一覧。ここに登録した種類が「＋ データ元を追加」のメニューに並び、.lc に保存できるようになる。
/// 新しい種類を足すときは、下の static コンストラクタに Register を1行足す（docs/データ元の追加方法.md 参照）。
/// </summary>
public static class DataSourceRegistry
{
    private static readonly List<DataSourceKind> _kinds = new();

    static DataSourceRegistry()
    {
        // ---- 組み込みのデータ元 ----
        Register<ExcelFileSourceSettings>("エクセルファイル", "📗",
            () => new ExcelFileSourceSettings { KeyColumns = { new TextItem("ID") } },
            s => new ExcelFileReader(s));

        Register<ExcelFolderSourceSettings>("エクセルフォルダ", "📊",
            () => new ExcelFolderSourceSettings { KeyColumns = { new TextItem("ID") } },
            s => new ExcelFolderReader(s));

        Register<FileListSourceSettings>("フォルダのファイル一覧", "📁",
            () => new FileListSourceSettings { Columns = { new ColumnMapping(FileListReader.ColumnFileName) } },
            s => new FileListReader(s));
    }

    /// <summary>登録済みの種類（メニューに並ぶ順）</summary>
    public static IReadOnlyList<DataSourceKind> Kinds => _kinds;

    /// <summary>.lc の読み書きで使う、設定クラスの一覧</summary>
    public static Type[] SettingsTypes => _kinds.Select(k => k.SettingsType).ToArray();

    /// <summary>
    /// データ元の種類を登録する。.lc を初めて読み書きする前（App の起動時など）に呼ぶこと
    /// </summary>
    public static void Register<TSettings>(string displayName, string icon,
                                           Func<TSettings> create, Func<TSettings, IDataSourceReader> createReader)
        where TSettings : SourceSettings
    {
        if (_kinds.Any(k => k.SettingsType == typeof(TSettings)))
            throw new InvalidOperationException($"{typeof(TSettings).Name} は登録済みです");

        _kinds.Add(new DataSourceKind
        {
            DisplayName = displayName,
            Icon = icon,
            SettingsType = typeof(TSettings),
            Create = () => create(),
            CreateReader = s => createReader((TSettings)s),
        });
    }

    /// <summary>設定に対応する種類。登録されていなければ null</summary>
    public static DataSourceKind? Find(SourceSettings settings) =>
        _kinds.FirstOrDefault(k => k.SettingsType == settings.GetType());
}
