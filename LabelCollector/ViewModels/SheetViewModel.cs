using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LabelCollector.Models;
using LabelCollector.Services;

namespace LabelCollector.ViewModels;

/// <summary>表の列 1つ分</summary>
public record SheetColumn(string Name, bool IsManual);

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

        RefreshCommand = new RelayCommand(Refresh);
        RemoveMissingCommand = new RelayCommand(RemoveMissing);

        Sources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Icon));
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

        if (columns.SequenceEqual(Columns)) return;
        Columns = columns;
        ColumnsChanged?.Invoke();
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
                combiner.Add(records);
            }
            LogDetails("キー重複（先のものを使用）", combiner.Duplicates, LogLevel.Warn);

            var records2 = combiner.ToRecords();
            var alreadyMissing = Rows.Where(r => r.State == RowState.Missing).Select(r => r.Key).ToHashSet();
            var result = RowMerger.Merge(Rows, records2, DateTime.Now);

            Rows.Clear();
            foreach (var r in result.Rows) Rows.Add(r);

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
        Status = $"消えた行を {targets.Count} 行削除しました";
        AppLogger.Instance.Info($"[{Name}] {Status}");
        _requestSave();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
