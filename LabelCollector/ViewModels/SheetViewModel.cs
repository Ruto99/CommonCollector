using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Windows.Input;
using LabelCollector.Models;
using LabelCollector.Models.Sources;
using LabelCollector.Services;

namespace LabelCollector.ViewModels;

/// <summary>表の列 1つ分</summary>
public record SheetColumn(string Name, bool IsManual);

/// <summary>
/// シートの画面のタブ 1つ分。エクセルのデータ元なら、親のタブはデータ元のフォルダ、子のタブ（Children）は取得元のファイルごと。
/// 子のタブがあれば、親のタブは子のタブを並べ、無ければ Filter に合う行の表を出す
/// </summary>
public record SheetTab(string Id, string Header, string ToolTip, Func<RowItem, bool> Filter,
                       IReadOnlyList<SheetColumn> Columns, IReadOnlyList<SheetTab> Children)
{
    /// <summary>タブを作り直すかどうかの判定用（Filter は比べない）</summary>
    public string Signature =>
        $"{Id}|{Header}|{ToolTip}|{string.Join(",", Columns)}|[{string.Join(";", Children.Select(c => c.Signature))}]";
}

/// <summary>1つのシート（表）。データ元の一覧から行を集め、手入力の列と一緒に管理する</summary>
public class SheetViewModel : INotifyPropertyChanged
{
    private readonly Action _requestSave;

    public SheetData Data { get; }
    public ObservableCollection<RowItem> Rows { get; } = new();

    /// <summary>データ元の一覧（プロパティウィンドウで追加・削除・編集する）</summary>
    public ObservableCollection<SourceSettings> Sources => Data.Sources;

    /// <summary>手入力の列の名前</summary>
    public ObservableCollection<TextItem> ManualColumns => Data.ManualColumns;

    public ICommand RefreshCommand { get; }
    public ICommand RemoveMissingCommand { get; }

    public SheetViewModel(SheetData data, Action requestSave)
    {
        Data = data;
        _requestSave = requestSave;
        foreach (var r in data.Rows) Rows.Add(r);
        InferPlaces();

        RefreshCommand = new RelayCommand(Refresh);
        RemoveMissingCommand = new RelayCommand(RemoveMissing);

        Sources.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Icon));
            UpdateColumns();
        };
        UpdateColumns();
    }

    public string Name
    {
        get => Data.Name;
        set { if (Data.Name == value) return; Data.Name = value; OnPropertyChanged(); }
    }

    /// <summary>エクスプローラーに出すアイコン（最初のデータ元の種類のもの）</summary>
    public string Icon => Sources.Count > 0 && DataSourceRegistry.Find(Sources[0]) is { } kind ? kind.Icon : "📋";

    // ---- 表の列 ----

    /// <summary>
    /// 表の列：データ元から取り込む列（自動）→ 手入力の列 の順。
    /// 状態・キー・取得元・最終確認は、この一覧とは別に必ず表示する
    /// </summary>
    public IReadOnlyList<SheetColumn> Columns { get; private set; } = [];

    /// <summary>列の構成が変わった（表の列を作り直す）</summary>
    public event Action? ColumnsChanged;

    /// <summary>データ元・手入力の列の設定から、表の列を作り直す</summary>
    public void UpdateColumns()
    {
        var manual = ManualColumns.Select(c => c.Value.Trim()).Where(n => n.Length > 0)
                                  .Distinct().ToList();
        var auto = Sources.SelectMany(s => s.Columns).Select(m => m.TargetName)
                          .Where(n => n.Length > 0 && !manual.Contains(n))
                          .Distinct().ToList();
        var columns = auto.Select(n => new SheetColumn(n, false))
                          .Concat(manual.Select(n => new SheetColumn(n, true)))
                          .ToList();

        if (!columns.SequenceEqual(Columns))
        {
            Columns = columns;
            ColumnsChanged?.Invoke();
        }
        UpdateTabs();
    }

    // ---- 画面のタブ（親：フォルダ、子：ファイル） ----

    /// <summary>データ元を読んでいない行（以前の .lc の行など）を出すタブの ID</summary>
    private const string UnsortedTabId = "*unsorted";

    /// <summary>
    /// 画面のタブ。エクセルのデータ元なら、親のタブはデータ元のフォルダ、子のタブは取得元のファイルごと（サブフォルダのファイルも同じ段に並べる）。
    /// ファイルを持たないデータ元（フォルダのファイル一覧など）は、データ元ごとに1つのタブ
    /// </summary>
    public IReadOnlyList<SheetTab> Tabs { get; private set; } = [];

    /// <summary>画面で選んでいるタブ（親のタブの ID → 選んでいる子のタブの ID。一番上は ""）。保存はしない</summary>
    public Dictionary<string, string> SelectedTabs { get; } = new();

    /// <summary>タブの構成が変わった（画面のタブを作り直す）</summary>
    public event Action? TabsChanged;

    /// <summary>データ元・行の場所から、タブを作り直す。構成が変わったときだけ TabsChanged を出す</summary>
    public void UpdateTabs()
    {
        var manual = Columns.Where(c => c.IsManual).ToList();
        var tabs = new List<SheetTab>();
        foreach (var source in Sources)
        {
            var id = source.Id;
            var targets = source.Columns.Select(m => m.TargetName).ToHashSet();
            var columns = Columns.Where(c => !c.IsManual && targets.Contains(c.Name)).Concat(manual).ToList();
            var kind = DataSourceRegistry.Find(source);

            // 親のタブにするフォルダ（エクセルフォルダはそのフォルダ、エクセルファイルはファイルのあるフォルダ）
            var baseFolder = source switch
            {
                ExcelFolderSourceSettings f => f.FolderPath,
                ExcelFileSourceSettings f => Path.GetDirectoryName(f.FilePath) ?? "",
                _ => null,
            };
            if (baseFolder == null)
            {
                tabs.Add(new SheetTab(id, $"{kind?.Icon ?? "📋"} {SourceEditing.SuggestName(source, kind?.DisplayName ?? "データ元")}",
                                      $"{kind?.DisplayName ?? source.GetType().Name}: {source.Location}",
                                      r => r.Places.Any(p => p.SourceId == id), columns, []));
                continue;
            }

            // 取得元のファイル（直下を先に、サブフォルダを後に、名前順）。サブフォルダのファイルも区別せず1段に並べる
            var files = Rows.SelectMany(r => r.Places).Where(p => p.SourceId == id && p.File.Length > 0)
                            .Select(p => p.File).Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(f => f.Count(c => c is '\\' or '/')).ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                            .ToList();
            // 別のフォルダに同じ名前のファイルがあるときだけ、見出しを相対パスにする
            var sameNames = files.GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                                 .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var children = files.Select(file => new SheetTab(
                Id: $"{id}|{file}",
                Header: $"📗 {(sameNames.Contains(Path.GetFileName(file)) ? file : Path.GetFileName(file))}",
                ToolTip: Path.Combine(baseFolder, file),
                Filter: r => r.Places.Any(p => p.SourceId == id && string.Equals(p.File, file, StringComparison.OrdinalIgnoreCase)),
                Columns: columns, Children: [])).ToList();

            var baseName = Path.GetFileName(baseFolder.TrimEnd('\\', '/'));
            tabs.Add(new SheetTab(id, $"📂 {(baseName.Length > 0 ? baseName : baseFolder)}",
                                  $"{kind?.DisplayName ?? source.GetType().Name}: {source.Location}",
                                  r => r.Places.Any(p => p.SourceId == id), columns, children));
        }

        // どのデータ元のタブにも入らない行（更新前の行や、削除したデータ元の行）
        var ids = Sources.Select(s => s.Id).ToHashSet();
        if (Rows.Any(r => !r.Places.Any(p => ids.Contains(p.SourceId))))
        {
            tabs.Add(new SheetTab(UnsortedTabId, "（未分類）",
                "データ元が分からない行です（以前の中間データの行、削除したデータ元の行など）。更新すると、データ元のタブに分かれます",
                r => !r.Places.Any(p => ids.Contains(p.SourceId)), Columns, []));
        }

        if (tabs.Select(t => t.Signature).SequenceEqual(Tabs.Select(t => t.Signature))) return;
        Tabs = tabs;
        TabsChanged?.Invoke();
    }

    /// <summary>
    /// 読んだ場所（Places）が無い行（以前の .lc の行）は、取得元の文字（「ファイル / シート名」）から
    /// どのデータ元のどのファイルの行かを推定する。推定できない行は「（未分類）」タブに出る
    /// </summary>
    private void InferPlaces()
    {
        foreach (var row in Rows.Where(r => r.Places.Count == 0))
        {
            var places = new List<RowPlace>();
            foreach (var part in row.Origin.Split(" ; ", StringSplitOptions.RemoveEmptyEntries))
            {
                // エクセルは「ファイル / シート名」、フォルダのファイル一覧は相対パスだけ（ファイル名に / は使えない）
                var sep = part.IndexOf(" / ", StringComparison.Ordinal);
                var file = (sep >= 0 ? part[..sep] : part).Trim();
                if (file.Length == 0) continue;
                if (FindSourceOf(file, isExcel: sep >= 0) is { } place && !places.Contains(place)) places.Add(place);
            }
            if (places.Count > 0) row.Places = places;
        }
    }

    /// <summary>取得元のファイルから、読んだデータ元を探す。見つからなければ null</summary>
    private RowPlace? FindSourceOf(string file, bool isExcel)
    {
        // ファイルが今もあるデータ元を優先する
        foreach (var source in Sources)
        {
            switch (source)
            {
                case ExcelFolderSourceSettings f when isExcel && ExistsIn(f.FolderPath, file):
                    return new RowPlace(source.Id, file);
                case ExcelFileSourceSettings f when isExcel && string.Equals(Path.GetFileName(f.FilePath), file, StringComparison.OrdinalIgnoreCase):
                    return new RowPlace(source.Id, file);
                case FileListSourceSettings f when !isExcel && ExistsIn(f.FolderPath, file):
                    return new RowPlace(source.Id, "");
            }
        }

        // ファイルが消えている（消えた行など）ときは、同じ種類のデータ元が1つだけならそれとみなす
        var candidates = Sources.Where(s => isExcel ? s is ExcelFolderSourceSettings : s is FileListSourceSettings).ToList();
        return candidates.Count == 1 ? new RowPlace(candidates[0].Id, isExcel ? file : "") : null;
    }

    private static bool ExistsIn(string folder, string relative)
    {
        try { return folder.Length > 0 && File.Exists(Path.Combine(folder, relative)); }
        catch { return false; } // パスに使えない文字など
    }

    // ---- データ元の追加・削除 ----

    /// <summary>データ元を追加する</summary>
    public void AddSource(SourceSettings source)
    {
        Sources.Add(source);
        AppLogger.Instance.Info($"[{Name}] データ元を追加: {KindName(source)} {source.Location}");
        RequestSave();
    }

    public void RemoveSource(SourceSettings source)
    {
        if (!Sources.Remove(source)) return;
        AppLogger.Instance.Info($"[{Name}] データ元を削除: {KindName(source)} {source.Location}");
        RequestSave();
    }

    private static string KindName(SourceSettings s) => DataSourceRegistry.Find(s)?.DisplayName ?? s.GetType().Name;

    // 左のエクスプローラー（ツリー）上の状態
    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

    private string _status = "";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    /// <summary>中間データ保存用に、表の内容を Data へ反映して返す</summary>
    public SheetData ToData()
    {
        Data.Rows = Rows.ToList();
        return Data;
    }

    /// <summary>
    /// 元に戻す・やり直しで、シートを履歴の状態にする。変わったところだけ入れ替える
    /// （行のキーと場所が同じなら、行は作り直さずに値だけ写すので、表のスクロール位置などはそのまま）
    /// </summary>
    public void Restore(SheetData state)
    {
        Name = state.Name;

        if (!ManualColumns.Select(c => c.Value).SequenceEqual(state.ManualColumns.Select(c => c.Value)))
        {
            ManualColumns.Clear();
            foreach (var c in state.ManualColumns) ManualColumns.Add(c);
        }
        Data.EnsureFixedManualColumns(); // 履歴から読んだ列には、消せない印が付いていないので付け直す

        if (LcFileStore.SourcesXml(Sources) != LcFileStore.SourcesXml(state.Sources))
        {
            Sources.Clear();
            foreach (var s in state.Sources) Sources.Add(s);
        }

        var sameRows = Rows.Count == state.Rows.Count
                       && Rows.Zip(state.Rows).All(p => p.First.Key == p.Second.Key
                                                        && p.First.Places.SequenceEqual(p.Second.Places));
        if (sameRows)
        {
            for (int i = 0; i < Rows.Count; i++) Rows[i].CopyFrom(state.Rows[i]);
        }
        else
        {
            Rows.Clear();
            foreach (var r in state.Rows) Rows.Add(r);
            Status = ""; // 「消えた行を 2 行削除しました」などは、もう今の表と合わない
        }

        UpdateColumns(); // タブも作り直す（構成が変わったときだけ）
    }

    /// <summary>設定や入力が変わったときに呼ぶ。表の列を作り直して保存する</summary>
    public void RequestSave()
    {
        UpdateColumns();
        _requestSave();
    }

    private const int MaxDetailLines = 20;

    /// <summary>全データ元を読み直して、表を更新する</summary>
    public void Refresh()
    {
        var log = AppLogger.Instance;
        try
        {
            if (Sources.Count == 0)
                throw new InvalidOperationException("データ元がありません。右のプロパティウィンドウの「＋ データ元を追加」で追加してください");

            // 全データ元を読んで、キーで1つにまとめる。1つでも読めなければ表は変えない
            var combiner = new RecordCombiner();
            foreach (var source in Sources)
            {
                var kind = DataSourceRegistry.Find(source)
                           ?? throw new InvalidOperationException($"未対応のデータ元です: {source.GetType().Name}");
                log.Info($"[{Name}] 読み込み: {kind.DisplayName} {source.Location}");
                var records = kind.CreateReader(source).Read();
                log.Info($"[{Name}]   {records.Count} 件");
                combiner.Add(records, source.Id);
            }
            LogDetails("キー重複（先のものを使用）", combiner.Duplicates, LogLevel.Warn);

            var records2 = combiner.ToRecords();
            var alreadyMissing = Rows.Where(r => r.State == RowState.Missing).Select(r => r.Key).ToHashSet();
            var result = RowMerger.Merge(Rows, records2, DateTime.Now);

            Rows.Clear();
            foreach (var r in result.Rows) Rows.Add(r);
            UpdateTabs(); // 新しいサブフォルダのタブなど

            LogDetails("追加", result.Rows.Where(r => r.State == RowState.New).Select(r => r.Key), LogLevel.Info);
            LogDetails("消えた", result.Rows.Where(r => r.State == RowState.Missing && !alreadyMissing.Contains(r.Key)).Select(r => r.Key), LogLevel.Warn);

            Status = $"更新: 追加 {result.Added} / 値の変更 {result.Updated} / " +
                     $"消えた行 {result.Missing} / 復活 {result.Restored}  (計 {Rows.Count} 行)";
            log.Info($"[{Name}] 更新完了: データ元 {Sources.Count} 件から {records2.Count} 行 / {Status}");
            RequestSave();
        }
        catch (Exception ex)
        {
            Status = "更新に失敗: " + ex.Message;
            log.Error($"[{Name}] 更新に失敗: {ex.Message}");
        }
    }

    private void LogDetails(string label, IEnumerable<string> keys, LogLevel level)
    {
        var list = keys.ToList();
        foreach (var key in list.Take(MaxDetailLines))
        {
            var msg = $"[{Name}]   {label}: {key}";
            if (level == LogLevel.Warn) AppLogger.Instance.Warn(msg); else AppLogger.Instance.Info(msg);
        }
        if (list.Count > MaxDetailLines)
        {
            var msg = $"[{Name}]   {label}: ほか {list.Count - MaxDetailLines} 件";
            if (level == LogLevel.Warn) AppLogger.Instance.Warn(msg); else AppLogger.Instance.Info(msg);
        }
    }

    private void RemoveMissing()
    {
        var targets = Rows.Where(r => r.State == RowState.Missing).ToList();
        foreach (var r in targets) Rows.Remove(r);
        UpdateTabs();
        Status = $"消えた行を {targets.Count} 行削除しました";
        AppLogger.Instance.Info($"[{Name}] {Status}");
        _requestSave();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
