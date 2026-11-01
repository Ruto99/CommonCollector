using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using LabelCollector.Models;

namespace LabelCollector.ViewModels;

/// <summary>プロパティウィンドウに並ぶ、エクセルの読み込み設定 1件分</summary>
public class ExcelSourceViewModel : INotifyPropertyChanged
{
    private readonly Action _changed;

    public ExcelSourceData Data { get; }

    public ICommand BrowseCommand { get; }
    public ICommand RemoveCommand { get; }

    public ExcelSourceViewModel(ExcelSourceData data, Action<ExcelSourceViewModel> remove, Action changed)
    {
        Data = data;
        _changed = changed;
        BrowseCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFolderDialog { Title = "エクセルのフォルダを選択" };
            if (Directory.Exists(FolderPath)) dlg.InitialDirectory = FolderPath;
            if (dlg.ShowDialog() != true) return;
            FolderPath = dlg.FolderName;
            changed();
        });
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    /// <summary>エクセルファイルが入っているフォルダ</summary>
    public string FolderPath
    {
        get => Data.FolderPath;
        set { if (Data.FolderPath == value) return; Data.FolderPath = value; OnPropertyChanged(); }
    }

    /// <summary>キーにする列名</summary>
    public string KeyColumn
    {
        get => Data.KeyColumn;
        set { if (Data.KeyColumn == value) return; Data.KeyColumn = value; OnPropertyChanged(); }
    }

    /// <summary>入力が終わったときに保存する</summary>
    public void RequestSave() => _changed();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
