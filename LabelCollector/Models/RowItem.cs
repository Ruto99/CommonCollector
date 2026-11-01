using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace LabelCollector.Models;

/// <summary>行の状態</summary>
public enum RowState
{
    /// <summary>データ元に存在する</summary>
    Normal,
    /// <summary>今回の更新で新しく追加された</summary>
    New,
    /// <summary>データ元から消えた（B列の値は保持している）</summary>
    Missing,
}

/// <summary>
/// 表の1行。Key で行を識別するので、A列が増減しても B列の値は Key に結びついたまま残る。
/// </summary>
public class RowItem : INotifyPropertyChanged
{
    private string _key = "";
    private string _columnA = "";
    private string _columnB = "";
    private RowState _state = RowState.New;
    private DateTime _lastSeen;

    /// <summary>行を識別する一意キー（ファイルの相対パス、エクセルのキー列の値など）</summary>
    [XmlAttribute("key")]
    public string Key { get => _key; set => Set(ref _key, value); }

    /// <summary>A列：データ元から自動更新される値</summary>
    public string ColumnA { get => _columnA; set => Set(ref _columnA, value); }

    /// <summary>B列：手動で入力する値</summary>
    public string ColumnB { get => _columnB; set => Set(ref _columnB, value); }

    [XmlAttribute("state")]
    public RowState State { get => _state; set => Set(ref _state, value); }

    /// <summary>最後にデータ元で見つかった日時</summary>
    public DateTime LastSeen { get => _lastSeen; set => Set(ref _lastSeen, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
