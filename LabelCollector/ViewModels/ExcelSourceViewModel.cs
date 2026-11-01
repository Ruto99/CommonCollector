using System.Collections.ObjectModel;
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
    public ICommand AddKeyColumnCommand { get; }

    /// <summary>キー列名の候補（複数指定できる）。編集すると Data.KeyColumns に反映する</summary>
    public ObservableCollection<KeyColumnViewModel> KeyColumns { get; } = new();

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
        AddKeyColumnCommand = new RelayCommand(() => AddKeyColumn(""));

        foreach (var name in data.KeyColumns) KeyColumns.Add(new KeyColumnViewModel(this, name));
    }

    /// <summary>キー列名の候補を1つ追加する（入力欄が増える）</summary>
    public void AddKeyColumn(string name)
    {
        KeyColumns.Add(new KeyColumnViewModel(this, name));
        SyncKeyColumns();
    }

    internal void RemoveKeyColumn(KeyColumnViewModel key)
    {
        KeyColumns.Remove(key);
        SyncKeyColumns();
        _changed();
    }

    /// <summary>画面の入力内容を、保存するデータ（空欄は除く）に反映する</summary>
    internal void SyncKeyColumns()
    {
        Data.KeyColumns = KeyColumns.Select(k => k.Name.Trim()).Where(k => k.Length > 0).ToList();
        OnPropertyChanged(nameof(KeyColumnsText));
    }

    /// <summary>ログ用：キー列名の候補を「, 」でつないだもの</summary>
    public string KeyColumnsText => Data.KeyColumns.Count == 0 ? "（未指定）" : string.Join(", ", Data.KeyColumns);

    /// <summary>エクセルファイルが入っているフォルダ</summary>
    public string FolderPath
    {
        get => Data.FolderPath;
        set { if (Data.FolderPath == value) return; Data.FolderPath = value; OnPropertyChanged(); }
    }

    /// <summary>入力が終わったときに保存する</summary>
    public void RequestSave() => _changed();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>キー列名の候補 1つ分（入力欄と削除ボタン）</summary>
public class KeyColumnViewModel : INotifyPropertyChanged
{
    private readonly ExcelSourceViewModel _owner;
    private string _name;

    public KeyColumnViewModel(ExcelSourceViewModel owner, string name)
    {
        _owner = owner;
        _name = name;
        RemoveCommand = new RelayCommand(() => _owner.RemoveKeyColumn(this));
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            _owner.SyncKeyColumns();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public ICommand RemoveCommand { get; }

    /// <summary>入力が終わったときに保存する</summary>
    public void RequestSave() => _owner.RequestSave();

    public event PropertyChangedEventHandler? PropertyChanged;
}
