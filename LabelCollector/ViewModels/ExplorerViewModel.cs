using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using LabelCollector.Services;

namespace LabelCollector.ViewModels;

/// <summary>
/// 左側のフォルダエクスプローラー。作業フォルダの中のフォルダと中間データ（.lc）を表示する。
/// 開いている中間データの下には、そのシートが並ぶ。
/// </summary>
public class ExplorerViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;

    public ExplorerViewModel(MainViewModel main, string rootPath)
    {
        _main = main;
        _rootPath = rootPath;
        Reload();
    }

    /// <summary>ツリーの一番上（作業フォルダ1つだけ）</summary>
    public ObservableCollection<ExplorerNode> Roots { get; } = new();

    private string _rootPath;
    /// <summary>作業フォルダ</summary>
    public string RootPath
    {
        get => _rootPath;
        set { _rootPath = value; OnPropertyChanged(); Reload(); }
    }

    internal ObservableCollection<SheetViewModel> Sheets => _main.Sheets;

    internal bool IsCurrentFile(string path) =>
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(_main.FilePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>ツリーを読み直す。開いていたフォルダは開いたままにする</summary>
    public void Reload()
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots) CollectExpanded(root, expanded);

        Roots.Clear();
        if (!Directory.Exists(RootPath))
        {
            AppLogger.Instance.Warn($"作業フォルダが見つかりません: {RootPath}");
            return;
        }

        var rootNode = new FolderNode(this, RootPath, isRoot: true);
        Roots.Add(rootNode);
        rootNode.IsExpanded = true;
        RestoreExpanded(rootNode, expanded);
    }

    private static void CollectExpanded(ExplorerNode node, HashSet<string> set)
    {
        if (!node.IsExpanded) return;
        set.Add(node.FullPath);
        foreach (var child in node.Children.OfType<ExplorerNode>()) CollectExpanded(child, set);
    }

    private static void RestoreExpanded(ExplorerNode node, HashSet<string> set)
    {
        foreach (var child in node.Children.OfType<ExplorerNode>().ToList())
        {
            // 開いている中間データは、シートが見えるよう常に展開する
            if (set.Contains(child.FullPath) || child is DataFileNode { IsCurrent: true })
            {
                child.IsExpanded = true;
                RestoreExpanded(child, set);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>エクスプローラーの1項目（フォルダ or 中間データ）</summary>
public abstract class ExplorerNode : INotifyPropertyChanged
{
    protected ExplorerNode(string fullPath) => FullPath = fullPath;

    public string FullPath { get; }
    public virtual string Name => Path.GetFileName(FullPath.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : FullPath;
    public abstract string Icon { get; }
    public virtual bool IsBold => false;
    public virtual IEnumerable Children => Array.Empty<object>();

    /// <summary>この項目が入っているフォルダ（フォルダ自身なら自分）</summary>
    public abstract string FolderPath { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (value) OnExpanded();
            OnPropertyChanged();
        }
    }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

    protected virtual void OnExpanded() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class FolderNode : ExplorerNode
{
    private readonly ExplorerViewModel _owner;
    private readonly ObservableCollection<object> _children = new();
    private bool _loaded;

    public FolderNode(ExplorerViewModel owner, string path, bool isRoot = false) : base(path)
    {
        _owner = owner;
        IsRoot = isRoot;
        // 中身は開いたときに読む。それまでは展開の▶を出すための仮の項目を入れておく
        if (HasEntries()) _children.Add(LoadingNode.Instance);
    }

    public bool IsRoot { get; }
    public override string Icon => IsRoot ? "🏠" : "📁";
    public override string FolderPath => FullPath;
    public override IEnumerable Children => _children;

    protected override void OnExpanded()
    {
        if (!_loaded) Load();
    }

    /// <summary>中身を読み直す</summary>
    public void Load()
    {
        _loaded = true;
        _children.Clear();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(FullPath)
                         .Where(IsVisible)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                _children.Add(new FolderNode(_owner, dir));

            foreach (var file in EnumerateDataFiles(FullPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                _children.Add(new DataFileNode(_owner, file));
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"フォルダを読めませんでした: {FullPath} ({ex.Message})");
        }
    }

    private bool HasEntries()
    {
        try
        {
            return Directory.EnumerateDirectories(FullPath).Any(IsVisible) || EnumerateDataFiles(FullPath).Any();
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateDataFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*" + LcFileStore.Extension)
                 // "*.lc" は Windows では "*.lcx" なども拾うので、拡張子を確かめる
                 .Where(f => string.Equals(Path.GetExtension(f), LcFileStore.Extension, StringComparison.OrdinalIgnoreCase))
                 .Where(IsVisible);

    private static bool IsVisible(string path)
    {
        if (Path.GetFileName(path).StartsWith('.')) return false;
        try
        {
            return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>中間データ（.lc）。開いているものは太字で、下にシートが並ぶ</summary>
public class DataFileNode(ExplorerViewModel owner, string path) : ExplorerNode(path)
{
    public bool IsCurrent => owner.IsCurrentFile(FullPath);
    public override string Icon => IsCurrent ? "📗" : "📄";
    public override bool IsBold => IsCurrent;
    public override string FolderPath => Path.GetDirectoryName(FullPath) ?? "";
    public override IEnumerable Children => IsCurrent ? owner.Sheets : Array.Empty<object>();
}

/// <summary>フォルダを開くまでの仮の項目</summary>
public class LoadingNode
{
    public static LoadingNode Instance { get; } = new();
    public string Name => "読み込み中…";
}
