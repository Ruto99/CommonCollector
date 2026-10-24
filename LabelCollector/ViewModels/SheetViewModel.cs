using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LabelCollector.Models;
using LabelCollector.Services;

namespace LabelCollector.ViewModels;

/// <summary>1つのシート（フォルダ or DB 1件分の表）</summary>
public class SheetViewModel : INotifyPropertyChanged
{
    private readonly Action _requestSave;

    public SheetData Data { get; }
    public ObservableCollection<RowItem> Rows { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand RemoveMissingCommand { get; }

    public SheetViewModel(SheetData data, Action requestSave)
    {
        Data = data;
        _requestSave = requestSave;
        foreach (var r in data.Rows) Rows.Add(r);

        RefreshCommand = new RelayCommand(Refresh);
        RemoveMissingCommand = new RelayCommand(RemoveMissing);
    }

    public string Name
    {
        get => Data.Name;
        set { if (Data.Name == value) return; Data.Name = value; OnPropertyChanged(); }
    }

    public bool IsFolder => Data.Type == SourceType.Folder;
    public bool IsDb => Data.Type == SourceType.Sqlite;
    public string Icon => IsFolder ? "📁" : "🗄";

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

    public string DbPath
    {
        get => Data.DbPath;
        set { Data.DbPath = value; OnPropertyChanged(); }
    }

    public string DbQuery
    {
        get => Data.DbQuery;
        set { Data.DbQuery = value; OnPropertyChanged(); }
    }

    private string _status = "";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    /// <summary>XML保存用に、表の内容を Data へ反映して返す</summary>
    public SheetData ToData()
    {
        Data.Rows = Rows.ToList();
        return Data;
    }

    public void RequestSave() => _requestSave();

    private const int MaxDetailLines = 20;

    public void Refresh()
    {
        var log = AppLogger.Instance;
        try
        {
            var target = IsFolder ? Data.FolderPath : Data.DbPath;
            log.Info($"[{Name}] 更新開始: {target}");

            IDataSource source = IsFolder
                ? new FolderDataSource(Data.FolderPath, Data.FilePattern)
                : new SqliteDataSource(Data.DbPath, Data.DbQuery);

            var records = source.Fetch();
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
