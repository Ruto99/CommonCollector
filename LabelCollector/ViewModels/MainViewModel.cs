using System.Collections.ObjectModel;
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

    private string _xmlPath = "";

    /// <summary>中間データ（XML）の保存先。既定は exe と同じフォルダの Workspace.xml</summary>
    public string XmlPath
    {
        get => _xmlPath;
        private set
        {
            _xmlPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            Explorer?.Reload(); // 開いている中間データの表示（太字・シート）を切り替える
        }
    }

    public string WindowTitle => $"LabelCollector - {Path.GetFileName(XmlPath)}";

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

    private string _status = "";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    public ICommand AddFolderCommand { get; }
    public ICommand AddExcelCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand RemoveSheetCommand { get; }
    public ICommand RefreshAllCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand OpenXmlCommand { get; }
    public ICommand ExportSheetCommand { get; }
    public ICommand ImportSheetsCommand { get; }
    public ICommand ChangeWorkDirectoryCommand { get; }

    private const string XmlFilter = "XMLファイル (*.xml)|*.xml|すべてのファイル (*.*)|*.*";

    public MainViewModel()
    {
        SaveAsCommand = new RelayCommand(SaveAs);
        OpenXmlCommand = new RelayCommand(OpenXml);
        ExportSheetCommand = new RelayCommand(ExportSheet);
        ImportSheetsCommand = new RelayCommand(ImportSheets);
        XmlPath = !string.IsNullOrWhiteSpace(_settings.WorkspacePath)
            ? _settings.WorkspacePath
            : Path.Combine(AppContext.BaseDirectory, "Workspace.xml");

        AddFolderCommand = new RelayCommand(AddFolder);
        AddExcelCommand = new RelayCommand(AddExcel);
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
            : Path.GetDirectoryName(Path.GetFullPath(XmlPath)) ?? AppContext.BaseDirectory;
        Explorer = new ExplorerViewModel(this, workDir);
    }

    private void Load()
    {
        try
        {
            var data = XmlStore.Load(XmlPath);
            foreach (var s in data.Sheets) Sheets.Add(new SheetViewModel(s, () => Save()));
            Status = File.Exists(XmlPath) ? $"読み込み完了: {Sheets.Count} シート ({XmlPath})" : "新規作成";
            Log.Info(Status);
        }
        catch (Exception ex)
        {
            Status = "XMLの読み込みに失敗: " + ex.Message;
            Log.Error(Status);
            QuarantineBrokenFile(XmlPath);
        }
    }

    /// <summary>壊れたXMLは、次の自動保存で上書きされないよう別名に退避する</summary>
    private static void QuarantineBrokenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var moved = path + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(path, moved);
            Log.Warn($"読み込めないXMLを退避しました: {moved}");
        }
        catch (Exception ex)
        {
            Log.Error("壊れたXMLの退避に失敗: " + ex.Message);
        }
    }

    /// <summary>名前を付けて保存：保存先を変更して、全シートをXMLに保存する</summary>
    private void SaveAs()
    {
        var dlg = new SaveFileDialog
        {
            Title = "中間データ（XML）を保存",
            Filter = XmlFilter,
            FileName = Path.GetFileName(XmlPath),
            InitialDirectory = Path.GetDirectoryName(XmlPath),
            DefaultExt = ".xml",
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true) return;

        XmlPath = dlg.FileName;
        _settings.WorkspacePath = XmlPath;
        _settings.Save();
        Save(manual: true);
    }

    /// <summary>XMLを開く：現在の内容をXMLの内容に入れ替える</summary>
    private void OpenXml()
    {
        var dlg = new OpenFileDialog { Title = "中間データ（XML）を開く", Filter = XmlFilter, CheckFileExists = true };
        if (dlg.ShowDialog() != true) return;
        OpenXmlFile(dlg.FileName);
    }

    /// <summary>指定したXMLを開いて、現在の内容と入れ替える</summary>
    public void OpenXmlFile(string path)
    {
        if (Explorer.IsCurrentXml(path)) return; // もう開いている

        TableData data;
        try
        {
            data = XmlStore.Load(path);
        }
        catch (Exception ex)
        {
            Status = "XMLの読み込みに失敗: " + ex.Message;
            Log.Error($"{Status} ({path})");
            return;
        }

        Save(); // 切り替え前の内容を現在のファイルに保存しておく
        OpenSheets.Clear();
        Sheets.Clear();
        SelectedSheet = null;
        foreach (var s in data.Sheets) Sheets.Add(new SheetViewModel(s, () => Save()));

        XmlPath = path;
        _settings.WorkspacePath = XmlPath;
        _settings.Save();
        Status = $"読み込み完了: {Sheets.Count} シート ({XmlPath})";
        Log.Info(Status);
    }

    /// <summary>フォルダに空の中間データ（XML）を作って開く</summary>
    /// <returns>作ったファイルのパス。作れなかったら null</returns>
    public string? CreateXml(string folder, string fileName)
    {
        fileName = fileName.Trim();
        if (fileName.Length == 0) return null;
        if (!fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) fileName += ".xml";
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
            XmlStore.Save(path, new TableData { UpdatedAt = DateTime.Now });
            Log.Info($"中間データを作成しました: {path}");
        }
        catch (Exception ex)
        {
            Status = "中間データの作成に失敗: " + ex.Message;
            Log.Error($"{Status} ({path})");
            return null;
        }

        OpenXmlFile(path); // 作ったものを開く（開くとツリーも読み直される）
        return path;
    }

    /// <summary>中間データ（XML）を削除する。開いているものは削除できない</summary>
    public void DeleteXml(string path)
    {
        if (Explorer.IsCurrentXml(path))
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

    /// <summary>選択中のシートの表を、単独のXMLファイルとして書き出す</summary>
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
            Title = $"「{sheet.Name}」の表をXMLに書き出し",
            Filter = XmlFilter,
            FileName = SafeFileName(sheet.Name) + ".xml",
            DefaultExt = ".xml",
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            XmlStore.Save(dlg.FileName, new TableData
            {
                UpdatedAt = DateTime.Now,
                Sheets = { sheet.ToData() },
            });
            Status = $"書き出しました: {dlg.FileName}";
            Log.Info($"[{sheet.Name}] 表をXMLに書き出し ({sheet.Rows.Count} 行): {dlg.FileName}");
        }
        catch (Exception ex)
        {
            Status = "書き出しに失敗: " + ex.Message;
            Log.Error($"[{sheet.Name}] {Status}");
        }
    }

    /// <summary>別のXMLからシートを追加する（既存のシートは残す）</summary>
    private void ImportSheets()
    {
        var dlg = new OpenFileDialog { Title = "XMLからシートを追加", Filter = XmlFilter, CheckFileExists = true };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var data = XmlStore.Load(dlg.FileName);
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
            Status = "XMLの読み込みに失敗: " + ex.Message;
            Log.Error($"{Status} ({dlg.FileName})");
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "sheet" : name;
    }

    private static AppLogger Log => AppLogger.Instance;

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "管理するフォルダを選択" };
        if (dlg.ShowDialog() != true) return;

        var sheet = Add(new SheetData
        {
            Name = UniqueName(Path.GetFileName(dlg.FolderName.TrimEnd('\\')) is { Length: > 0 } n ? n : dlg.FolderName),
            Type = SourceType.Folder,
            FolderPath = dlg.FolderName,
        });
        sheet.Refresh();
    }

    /// <summary>エクセルのシートを追加する。フォルダを選んだら、キー列名「ID」で1件目の指定を作る</summary>
    private void AddExcel()
    {
        var dlg = new OpenFolderDialog { Title = "エクセルファイルが入っているフォルダを選択" };
        if (dlg.ShowDialog() != true) return;

        var name = Path.GetFileName(dlg.FolderName.TrimEnd('\\')) is { Length: > 0 } n ? n : dlg.FolderName;
        var sheet = Add(new SheetData
        {
            Name = UniqueName(name),
            Type = SourceType.Excel,
            ExcelSources = { new ExcelSourceData { FolderPath = dlg.FolderName, KeyColumn = "ID" } },
        });
        sheet.Refresh();
    }

    private SheetViewModel Add(SheetData data)
    {
        var sheet = new SheetViewModel(data, () => Save());
        Sheets.Add(sheet);
        Log.Info($"シートを追加: {sheet.Name} ({(sheet.IsFolder ? data.FolderPath : string.Join(", ", data.ExcelSources.Select(x => x.FolderPath)))})");
        SelectedSheet = sheet;
        Open(sheet);
        Save();
        return sheet;
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (int i = 2; Sheets.Any(s => s.Name == name); i++) name = $"{baseName} ({i})";
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

        OpenSheets.Remove(sheet);
        Sheets.Remove(sheet);
        SelectedSheet = null;
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
            var isNewFile = !File.Exists(XmlPath);
            XmlStore.Save(XmlPath, new TableData
            {
                UpdatedAt = DateTime.Now,
                Sheets = Sheets.Select(s => s.ToData()).ToList(),
            });
            if (isNewFile) Explorer?.Reload(); // 新しくできたファイルをエクスプローラーに出す
            Status = $"保存しました ({DateTime.Now:HH:mm:ss})";
            if (manual) Log.Info($"保存しました: {XmlPath}");
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
