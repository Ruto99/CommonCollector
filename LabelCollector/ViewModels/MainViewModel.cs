using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using LabelCollector.Models;
using LabelCollector.Services;

namespace LabelCollector.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings = AppSettings.Load();

    private string _filePath = "";

    /// <summary>中間データ（.lc）の保存先。既定は exe と同じフォルダの Workspace.lc</summary>
    public string FilePath
    {
        get => _filePath;
        private set
        {
            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            Explorer?.Reload(); // 開いている中間データの表示（太字・シート）を切り替える
            // プロパティに開いているファイルを出していたら、新しいファイルのものに差し替える
            if (PropertyTarget is DataFileViewModel { IsCurrent: true }) ShowFileProperties(value);
        }
    }

    public string WindowTitle => $"LabelCollector - {Path.GetFileName(FilePath)}";

    /// <summary>左のフォルダエクスプローラー</summary>
    public ExplorerViewModel Explorer { get; }

    /// <summary>登録済みのシート（エクスプローラーで、開いている中間データの下に並ぶ）</summary>
    public ObservableCollection<SheetViewModel> Sheets { get; } = new();

    /// <summary>タブで開いているシート（AvalonDock のドキュメント）</summary>
    public ObservableCollection<SheetViewModel> OpenSheets { get; } = new();

    private SheetViewModel? _selectedSheet;
    public SheetViewModel? SelectedSheet
    {
        get => _selectedSheet;
        set
        {
            if (ReferenceEquals(_selectedSheet, value)) return;
            _selectedSheet = value;
            if (value != null) value.IsSelected = true; // エクスプローラーでも選択状態にする
            OnPropertyChanged();
        }
    }

    private object? _propertyTarget;
    /// <summary>右のプロパティウィンドウに表示するもの（中間データ DataFileViewModel か、シート SheetViewModel）</summary>
    public object? PropertyTarget
    {
        get => _propertyTarget;
        private set { _propertyTarget = value; OnPropertyChanged(); }
    }

    /// <summary>中間データ（.lc）の中身をプロパティに表示する</summary>
    public void ShowFileProperties(string path) => PropertyTarget = DataFileViewModel.Create(this, path);

    /// <summary>シートの設定をプロパティに表示する</summary>
    public void ShowSheetProperties(SheetViewModel sheet)
    {
        SelectedSheet = sheet;
        PropertyTarget = sheet;
    }

    public void ClearProperties() => PropertyTarget = null;

    private string _status = "";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    public ICommand OpenCommand { get; }
    public ICommand RemoveSheetCommand { get; }
    public ICommand RefreshAllCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand ExportSheetCommand { get; }
    public ICommand ImportSheetsCommand { get; }
    public ICommand ChangeWorkDirectoryCommand { get; }

    private const string FileFilter = LcFileStore.DialogFilter;

    public MainViewModel()
    {
        SaveAsCommand = new RelayCommand(SaveAs);
        OpenFileCommand = new RelayCommand(OpenFile);
        ExportSheetCommand = new RelayCommand(ExportSheet);
        ImportSheetsCommand = new RelayCommand(ImportSheets);
        FilePath = MigrateLegacyXml(!string.IsNullOrWhiteSpace(_settings.WorkspacePath)
            ? _settings.WorkspacePath
            : Path.Combine(AppContext.BaseDirectory, "Workspace" + LcFileStore.Extension));

        // シートが削除されたら、タブ・選択・プロパティからも外す
        Sheets.CollectionChanged += Sheets_CollectionChanged;

        OpenCommand = new RelayCommand(() => Open(SelectedSheet));
        RemoveSheetCommand = new RelayCommand(RemoveSheet);
        RefreshAllCommand = new RelayCommand(RefreshAll);
        SaveCommand = new RelayCommand(() => Save(manual: true));
        ChangeWorkDirectoryCommand = new RelayCommand(ChangeWorkDirectory);
        Log.Info("LabelCollector を起動しました");
        Load();

        // 作業フォルダ：未設定なら、開いている中間データのフォルダ
        var workDir = Directory.Exists(_settings.WorkDirectory)
            ? _settings.WorkDirectory
            : Path.GetDirectoryName(Path.GetFullPath(FilePath)) ?? AppContext.BaseDirectory;
        Explorer = new ExplorerViewModel(this, workDir);
    }

    /// <summary>
    /// 以前のバージョンの中間データ（.xml）を .lc にコピーして使う。元の .xml はそのまま残す
    /// </summary>
    private string MigrateLegacyXml(string path)
    {
        var lcPath = Path.ChangeExtension(path, LcFileStore.Extension);
        var xmlPath = Path.ChangeExtension(path, ".xml");
        try
        {
            if (!File.Exists(lcPath) && File.Exists(xmlPath))
            {
                File.Copy(xmlPath, lcPath);
                Log.Info($"以前の中間データ(.xml)を .lc にコピーしました: {xmlPath} → {lcPath}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"以前の中間データ(.xml)のコピーに失敗: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(_settings.WorkspacePath)
            && !string.Equals(_settings.WorkspacePath, lcPath, StringComparison.OrdinalIgnoreCase))
        {
            _settings.WorkspacePath = lcPath;
            _settings.Save();
        }
        return lcPath;
    }

    private void Sheets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var removed = e.Action == NotifyCollectionChangedAction.Reset
            ? OpenSheets.ToList()
            : e.OldItems?.OfType<SheetViewModel>().ToList() ?? [];
        foreach (var sheet in removed.Where(s => !Sheets.Contains(s)))
        {
            OpenSheets.Remove(sheet);
            if (ReferenceEquals(SelectedSheet, sheet)) SelectedSheet = null;
            if (ReferenceEquals(PropertyTarget, sheet)) PropertyTarget = null;
        }
        if (e.Action == NotifyCollectionChangedAction.Reset && PropertyTarget is SheetViewModel)
            PropertyTarget = null;
    }

    private void Load()
    {
        try
        {
            var data = LcFileStore.Load(FilePath);
            foreach (var s in data.Sheets) Sheets.Add(new SheetViewModel(s, () => Save()));
            Status = File.Exists(FilePath) ? $"読み込み完了: {Sheets.Count} シート ({FilePath})" : "新規作成";
            Log.Info(Status);
        }
        catch (Exception ex)
        {
            Status = "中間データの読み込みに失敗: " + ex.Message;
            Log.Error(Status);
            QuarantineBrokenFile(FilePath);
        }
    }

    /// <summary>壊れた中間データは、次の自動保存で上書きされないよう別名に退避する</summary>
    private static void QuarantineBrokenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var moved = path + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(path, moved);
            Log.Warn($"読み込めない中間データを退避しました: {moved}");
        }
        catch (Exception ex)
        {
            Log.Error("壊れた中間データの退避に失敗: " + ex.Message);
        }
    }

    /// <summary>名前を付けて保存：保存先を変更して、全シートを中間データに保存する</summary>
    private void SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Title = "中間データ（.lc）を保存",
            Filter = FileFilter,
            FileName = Path.GetFileName(FilePath),
            InitialDirectory = Path.GetDirectoryName(FilePath),
            DefaultExt = LcFileStore.Extension,
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true) return;

        FilePath = dlg.FileName;
        _settings.WorkspacePath = FilePath;
        _settings.Save();
        Save(manual: true);
    }

    /// <summary>中間データを開く：現在の内容を、そのファイルの内容に入れ替える</summary>
    private void OpenFile()
    {
        var dlg = new OpenFileDialog { Title = "中間データ（.lc）を開く", Filter = FileFilter, CheckFileExists = true };
        if (dlg.ShowDialog() != true) return;
        OpenDataFile(dlg.FileName);
    }

    /// <summary>指定した中間データを開いて、現在の内容と入れ替える</summary>
    public void OpenDataFile(string path)
    {
        if (Explorer.IsCurrentFile(path)) return; // もう開いている

        TableData data;
        try
        {
            data = LcFileStore.Load(path);
        }
        catch (Exception ex)
        {
            Status = "中間データの読み込みに失敗: " + ex.Message;
            Log.Error($"{Status} ({path})");
            return;
        }

        Save(); // 切り替え前の内容を現在のファイルに保存しておく
        OpenSheets.Clear();
        Sheets.Clear();
        SelectedSheet = null;
        foreach (var s in data.Sheets) Sheets.Add(new SheetViewModel(s, () => Save()));

        FilePath = path;
        _settings.WorkspacePath = FilePath;
        _settings.Save();
        Status = $"読み込み完了: {Sheets.Count} シート ({FilePath})";
        Log.Info(Status);

        // プロパティにこのファイルを出していたら、開いた状態のものに差し替える
        if (PropertyTarget is DataFileViewModel f && IsSamePath(f.FilePath, path)) ShowFileProperties(path);
    }

    private static bool IsSamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>フォルダに空の中間データ（.lc）を作って開く</summary>
    /// <returns>作ったファイルのパス。作れなかったら null</returns>
    public string? CreateDataFile(string folder, string fileName)
    {
        fileName = fileName.Trim();
        if (fileName.Length == 0) return null;
        fileName = LcFileStore.WithExtension(fileName);
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show($"ファイル名に使えない文字が入っています: {fileName}", "中間データの作成",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var path = Path.Combine(folder, fileName);
        if (File.Exists(path))
        {
            MessageBox.Show($"同じ名前のファイルがあります: {path}", "中間データの作成",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        try
        {
            LcFileStore.Save(path, new TableData { UpdatedAt = DateTime.Now });
            Log.Info($"中間データを作成しました: {path}");
        }
        catch (Exception ex)
        {
            Status = "中間データの作成に失敗: " + ex.Message;
            Log.Error($"{Status} ({path})");
            return null;
        }

        OpenDataFile(path); // 作ったものを開く（開くとツリーも読み直される）
        return path;
    }

    /// <summary>中間データ（.lc）を削除する。開いているものは削除できない</summary>
    public void DeleteDataFile(string path)
    {
        if (Explorer.IsCurrentFile(path))
        {
            MessageBox.Show("開いている中間データは削除できません。\n別の中間データを開いてから削除してください。",
                            "中間データの削除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ok = MessageBox.Show($"「{Path.GetFileName(path)}」を削除します。よろしいですか？\n\n{path}",
                                 "中間データの削除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            File.Delete(path);
            if (PropertyTarget is DataFileViewModel f && IsSamePath(f.FilePath, path)) PropertyTarget = null;
            Status = $"削除しました: {path}";
            Log.Warn($"中間データを削除しました: {path}");
        }
        catch (Exception ex)
        {
            Status = "削除に失敗: " + ex.Message;
            Log.Error($"{Status} ({path})");
        }
        Explorer.Reload();
    }

    /// <summary>エクスプローラーに表示する作業フォルダを変える</summary>
    private void ChangeWorkDirectory()
    {
        var dlg = new OpenFolderDialog { Title = "作業フォルダを選択" };
        if (Directory.Exists(Explorer.RootPath)) dlg.InitialDirectory = Explorer.RootPath;
        if (dlg.ShowDialog() != true) return;

        Explorer.RootPath = dlg.FolderName;
        _settings.WorkDirectory = dlg.FolderName;
        _settings.Save();
        Log.Info($"作業フォルダを変更しました: {dlg.FolderName}");
    }

    /// <summary>選択中のシートの表を、単独の中間データ（.lc）として書き出す</summary>
    private void ExportSheet()
    {
        var sheet = SelectedSheet;
        if (sheet == null)
        {
            Status = "書き出すシートを左のエクスプローラーで選択してください";
            Log.Warn(Status);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = $"「{sheet.Name}」の表を中間データ(.lc)に書き出し",
            Filter = FileFilter,
            FileName = SafeFileName(sheet.Name) + LcFileStore.Extension,
            DefaultExt = LcFileStore.Extension,
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            LcFileStore.Save(dlg.FileName, new TableData
            {
                UpdatedAt = DateTime.Now,
                Sheets = { sheet.ToData() },
            });
            Status = $"書き出しました: {dlg.FileName}";
            Log.Info($"[{sheet.Name}] 表を中間データに書き出し ({sheet.Rows.Count} 行): {dlg.FileName}");
        }
        catch (Exception ex)
        {
            Status = "書き出しに失敗: " + ex.Message;
            Log.Error($"[{sheet.Name}] {Status}");
        }
    }

    /// <summary>別の中間データからシートを追加する（既存のシートは残す）</summary>
    private void ImportSheets()
    {
        var dlg = new OpenFileDialog { Title = "中間データからシートを追加", Filter = FileFilter, CheckFileExists = true };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var data = LcFileStore.Load(dlg.FileName);
            foreach (var s in data.Sheets)
            {
                s.Name = UniqueName(string.IsNullOrWhiteSpace(s.Name) ? "シート" : s.Name);
                Sheets.Add(new SheetViewModel(s, () => Save()));
            }
            Save();
            Status = $"{data.Sheets.Count} シートを追加しました: {dlg.FileName}";
            Log.Info(Status);
        }
        catch (Exception ex)
        {
            Status = "中間データの読み込みに失敗: " + ex.Message;
            Log.Error($"{Status} ({dlg.FileName})");
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "sheet" : name;
    }

    private static AppLogger Log => AppLogger.Instance;

    /// <summary>
    /// データ元を1つ持つシートを追加する。場所（フォルダ・ファイル）を選んだら読み込む
    /// </summary>
    public void AddSheet(DataSourceKind kind)
    {
        var source = SourceEditing.CreateSource(kind);
        if (source == null) return;

        var data = SheetData.CreateNew(UniqueName(SourceEditing.SuggestName(source, kind.DisplayName)));
        data.Sources.Add(source);
        Add(data).Refresh();
    }

    private SheetViewModel Add(SheetData data)
    {
        var sheet = new SheetViewModel(data, () => Save());
        Sheets.Add(sheet);
        Log.Info($"シートを追加: {sheet.Name} ({string.Join(", ", data.Sources.Select(x => x.Location))})");
        SelectedSheet = sheet;
        Open(sheet);
        Save();
        return sheet;
    }

    private string UniqueName(string baseName) => UniqueName(Sheets, baseName);

    /// <summary>シート名が重複しないよう、必要なら (2) などを付ける</summary>
    internal static string UniqueName(IEnumerable<SheetViewModel> sheets, string baseName)
    {
        var names = sheets.Select(s => s.Name).ToHashSet();
        var name = baseName;
        for (int i = 2; names.Contains(name); i++) name = $"{baseName} ({i})";
        return name;
    }

    private void Open(SheetViewModel? sheet)
    {
        if (sheet == null) return;
        if (!OpenSheets.Contains(sheet)) OpenSheets.Add(sheet);
        // 既に開いている場合は AvalonDock 側で前面化する（MainWindow 側で処理）
        ActivateRequested?.Invoke(sheet);
    }

    public void OpenSheet(SheetViewModel sheet) => Open(sheet);

    /// <summary>既に開いているタブを前面に出すための通知</summary>
    public event Action<SheetViewModel>? ActivateRequested;

    private void RemoveSheet()
    {
        var sheet = SelectedSheet;
        if (sheet == null) return;

        var ok = MessageBox.Show(
            $"「{sheet.Name}」を一覧から削除します。\nこのシートの B列の入力内容も失われます。よろしいですか？",
            "シートの削除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;

        Sheets.Remove(sheet); // タブ・選択・プロパティからは Sheets_CollectionChanged で外れる
        Log.Warn($"シートを削除: {sheet.Name}");
        Save();
    }

    private void RefreshAll()
    {
        Log.Info($"全シート更新を開始 ({Sheets.Count} シート)");
        foreach (var s in Sheets) s.Refresh();
        Status = $"全 {Sheets.Count} シートを更新しました";
        Log.Info(Status);
    }

    /// <param name="manual">ボタン操作の保存のときだけログに残す（自動保存は頻繁なので残さない）</param>
    public void Save(bool manual = false)
    {
        try
        {
            var isNewFile = !File.Exists(FilePath);
            LcFileStore.Save(FilePath, new TableData
            {
                UpdatedAt = DateTime.Now,
                Sheets = Sheets.Select(s => s.ToData()).ToList(),
            });
            if (isNewFile) Explorer?.Reload(); // 新しくできたファイルをエクスプローラーに出す
            Status = $"保存しました ({DateTime.Now:HH:mm:ss})";
            if (manual) Log.Info($"保存しました: {FilePath}");
        }
        catch (Exception ex)
        {
            Status = "保存に失敗: " + ex.Message;
            Log.Error(Status);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

/// <summary>CommandParameter を受け取るコマンド</summary>
public class RelayCommand<T>(Action<T> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => parameter is T;
    public void Execute(object? parameter)
    {
        if (parameter is T value) execute(value);
    }
}
