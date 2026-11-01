using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LabelCollector.Models;
using LabelCollector.Services;

namespace LabelCollector.ViewModels;

/// <summary>1つのシート（フォルダ or エクセル 1件分の表）</summary>
public class SheetViewModel : INotifyPropertyChanged
{
    private readonly Action _requestSave;

    public SheetData Data { get; }
    public ObservableCollection<RowItem> Rows { get; } = new();

    /// <summary>エクセルの読み込み設定（フォルダ＋キー列名）。プロパティウィンドウで追加・削除する</summary>
    public ObservableCollection<ExcelSourceViewModel> ExcelSources { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand RemoveMissingCommand { get; }
    public ICommand AddExcelSourceCommand { get; }

    public SheetViewModel(SheetData data, Action requestSave)
    {
        Data = data;
        _requestSave = requestSave;
        foreach (var r in data.Rows) Rows.Add(r);
        foreach (var x in data.ExcelSources) ExcelSources.Add(CreateSource(x));

        if (data.IsUnknownType)
            AppLogger.Instance.Warn($"[{data.Name}] 未対応の種類「{data.TypeText}」のシートです。フォルダのシートとして扱います（B列の内容は残ります）");

        RefreshCommand = new RelayCommand(Refresh);
        RemoveMissingCommand = new RelayCommand(RemoveMissing);
        AddExcelSourceCommand = new RelayCommand(() => AddExcelSource());
    }

    private ExcelSourceViewModel CreateSource(ExcelSourceData data) =>
        new(data, RemoveExcelSource, _requestSave);

    /// <summary>エクセルの読み込み設定を1件追加する</summary>
    public ExcelSourceViewModel AddExcelSource(string folderPath = "")
    {
        var src = CreateSource(ExcelSourceData.CreateDefault(folderPath));
        ExcelSources.Add(src);
        _requestSave();
        return src;
    }

    private void RemoveExcelSource(ExcelSourceViewModel src)
    {
        ExcelSources.Remove(src);
        AppLogger.Instance.Info($"[{Name}] エクセルの指定を削除: {src.FolderPath} (キー列: {src.KeyColumnsText})");
        _requestSave();
    }

    public string Name
    {
        get => Data.Name;
        set { if (Data.Name == value) return; Data.Name = value; OnPropertyChanged(); }
    }

    public bool IsFolder => Data.Type == SourceType.Folder;
    public bool IsExcel => Data.Type == SourceType.Excel;
    public string Icon => IsFolder ? "📁" : "📊";
    public string TypeName => IsFolder ? "フォルダ（ファイル一覧）" : "エクセル";

    public string FolderPath
    {
        get => Data.FolderPath;
        set { Data.FolderPath = value; OnPropertyChanged(); }
    }

    public string FilePattern
    {
        get => Data.FilePattern;
        set { Data.FilePattern = value; OnPropertyChanged(); }
    }

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
        Data.ExcelSources = ExcelSources.Select(x => x.Data).ToList();
        return Data;
    }

    public void RequestSave() => _requestSave();

    private const int MaxDetailLines = 20;

    public void Refresh()
    {
        var log = AppLogger.Instance;
        try
        {
            var records = IsFolder ? FetchFolder() : FetchExcel();
            var alreadyMissing = Rows.Where(r => r.State == RowState.Missing).Select(r => r.Key).ToHashSet();

            var result = RowMerger.Merge(Rows, records, DateTime.Now);

            Rows.Clear();
            foreach (var r in result.Rows) Rows.Add(r);

            LogDetails("追加", result.Rows.Where(r => r.State == RowState.New).Select(r => r.Key), LogLevel.Info);
            LogDetails("消えた", result.Rows.Where(r => r.State == RowState.Missing && !alreadyMissing.Contains(r.Key)).Select(r => r.Key), LogLevel.Warn);

            Status = $"更新: 追加 {result.Added} / A列変更 {result.Updated} / " +
                     $"消えた行 {result.Missing} / 復活 {result.Restored}  (計 {Rows.Count} 行)";
            log.Info($"[{Name}] 更新完了: データ元 {records.Count} 件 / {Status}");
            _requestSave();
        }
        catch (Exception ex)
        {
            Status = "更新に失敗: " + ex.Message;
            log.Error($"[{Name}] 更新に失敗: {ex.Message}");
        }
    }

    private List<SourceRecord> FetchFolder()
    {
        AppLogger.Instance.Info($"[{Name}] 更新開始: {Data.FolderPath}");
        return new FolderDataSource(Data.FolderPath, Data.FilePattern).Fetch();
    }

    /// <summary>エクセルの指定をすべて読んで1つの表にまとめる（同じキーは先に見つかったほうを使う）</summary>
    private List<SourceRecord> FetchExcel()
    {
        if (ExcelSources.Count == 0)
            throw new InvalidOperationException("エクセルの指定がありません。右のプロパティウィンドウの「＋ 追加」で指定してください");

        var all = new List<SourceRecord>();
        foreach (var src in ExcelSources)
        {
            AppLogger.Instance.Info($"[{Name}] 更新開始: {src.FolderPath} (キー列: {src.KeyColumnsText})");
            var records = new ExcelDataSource(src.FolderPath, src.Data.KeyColumns).Fetch();
            AppLogger.Instance.Info($"[{Name}]   {records.Count} 件読み込み");
            all.AddRange(records);
        }

        var duplicates = all.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                            .Where(g => g.Count() > 1)
                            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.ValueA))})");
        LogDetails("キー重複（先のものを使用）", duplicates, LogLevel.Warn);
        return all;
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
