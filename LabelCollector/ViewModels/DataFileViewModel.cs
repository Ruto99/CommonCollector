using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using LabelCollector.Models;
using LabelCollector.Services;

namespace LabelCollector.ViewModels;

/// <summary>
/// プロパティウィンドウで編集する中間データ（.lc）1ファイル分の中身。
/// 開いているファイルなら画面のシートをそのまま編集し、開いていないファイルならファイルから読んで、変更のたびにそのファイルへ保存する。
/// </summary>
public class DataFileViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;

    private DataFileViewModel(MainViewModel main, string path, bool isCurrent)
    {
        _main = main;
        FilePath = path;
        IsCurrent = isCurrent;

        AddExcelSheetCommand = new RelayCommand(AddExcelSheet);
        OpenCommand = new RelayCommand(() => _main.OpenDataFile(FilePath));
        OpenSheetCommand = new RelayCommand<SheetViewModel>(s => _main.OpenSheet(s));
        RemoveSheetCommand = new RelayCommand<SheetViewModel>(RemoveSheet);
        AddExcelSourceCommand = new RelayCommand<SheetViewModel>(s => s.AddExcelSource());
    }

    /// <summary>ファイルの中身を編集するための ViewModel を作る</summary>
    public static DataFileViewModel Create(MainViewModel main, string path)
    {
        if (main.Explorer.IsCurrentFile(path))
        {
            // 開いているファイル：画面のシートと同じものを編集する
            var current = new DataFileViewModel(main, path, isCurrent: true) { Sheets = main.Sheets };
            current.UpdatedText = File.Exists(path) ? File.GetLastWriteTime(path).ToString("yyyy/MM/dd HH:mm:ss") : "（未保存）";
            return current;
        }

        var vm = new DataFileViewModel(main, path, isCurrent: false) { Sheets = new() };
        try
        {
            var data = LcFileStore.Load(path);
            foreach (var s in data.Sheets) vm.Sheets.Add(new SheetViewModel(s, vm.Save));
            vm.UpdatedText = data.UpdatedAt == default ? "-" : data.UpdatedAt.ToString("yyyy/MM/dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            // 壊れたファイルを上書きしないよう、編集できない状態にする
            vm.LoadError = "読み込めませんでした: " + ex.Message;
            AppLogger.Instance.Error($"中間データを読み込めませんでした: {path} ({ex.Message})");
        }
        return vm;
    }

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string FolderPath => Path.GetDirectoryName(FilePath) ?? "";

    /// <summary>いま画面で開いているファイルか</summary>
    public bool IsCurrent { get; }
    public bool IsNotCurrent => !IsCurrent;
    public string StateText => IsCurrent ? "開いています" : "開いていません（ここでの変更はファイルに直接保存されます）";

    public string UpdatedText { get; private set; } = "";

    /// <summary>読み込みに失敗したときのメッセージ。null なら正常</summary>
    public string? LoadError { get; private set; }
    public bool CanEdit => LoadError == null;

    public ObservableCollection<SheetViewModel> Sheets { get; private init; } = new();

    public ICommand AddExcelSheetCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand OpenSheetCommand { get; }
    public ICommand RemoveSheetCommand { get; }
    public ICommand AddExcelSourceCommand { get; }

    /// <summary>変更をファイルへ保存する</summary>
    public void Save()
    {
        if (!CanEdit) return;
        if (IsCurrent)
        {
            _main.Save();
            return;
        }

        try
        {
            LcFileStore.Save(FilePath, new TableData
            {
                UpdatedAt = DateTime.Now,
                Sheets = Sheets.Select(s => s.ToData()).ToList(),
            });
            UpdatedText = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            OnPropertyChanged(nameof(UpdatedText));
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"中間データの保存に失敗: {FilePath} ({ex.Message})");
        }
    }

    /// <summary>エクセルのシートを追加する。フォルダとキー列名は追加後にプロパティで指定する</summary>
    private void AddExcelSheet()
    {
        if (!CanEdit) return;
        var data = new SheetData
        {
            Name = MainViewModel.UniqueName(Sheets, "新しいシート"),
            Type = SourceType.Excel,
            ExcelSources = { new ExcelSourceData() },
        };
        Sheets.Add(new SheetViewModel(data, IsCurrent ? () => _main.Save() : Save));
        AppLogger.Instance.Info($"[{FileName}] シートを追加: {data.Name}");
        Save();
    }

    private void RemoveSheet(SheetViewModel sheet)
    {
        var ok = MessageBox.Show(
            $"「{FileName}」からシート「{sheet.Name}」を削除します。\nこのシートの B列の入力内容も失われます。よろしいですか？",
            "シートの削除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;

        Sheets.Remove(sheet); // 開いているファイルなら、タブも MainViewModel 側で閉じる
        AppLogger.Instance.Warn($"[{FileName}] シートを削除: {sheet.Name}");
        Save();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
