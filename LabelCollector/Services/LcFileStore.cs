using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using LabelCollector.Models;

namespace LabelCollector.Services;

/// <summary>
/// このツールの中間データ（拡張子 .lc）を読み書きする。中身は XML 形式
/// </summary>
public static class LcFileStore
{
    /// <summary>中間データの拡張子</summary>
    public const string Extension = ".lc";

    /// <summary>ファイルを開く・保存するダイアログ用のフィルター</summary>
    public const string DialogFilter = "LabelCollector 中間データ (*.lc)|*.lc|すべてのファイル (*.*)|*.*";

    /// <summary>ファイル名に .lc を付ける（付いていなければ）</summary>
    public static string WithExtension(string fileName) =>
        fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + Extension;

    private static readonly XmlSerializer Serializer = new(typeof(TableData));

    public static TableData Load(string path)
    {
        if (!File.Exists(path)) return new TableData();
        using var stream = File.OpenRead(path);
        return (TableData?)Serializer.Deserialize(stream) ?? new TableData();
    }

    public static void Save(string path, TableData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // 一時ファイルに書いてから置き換える（保存中に落ちても元のファイルが壊れない）
        var tmp = path + ".tmp";
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var writer = XmlWriter.Create(tmp, settings))
            Serializer.Serialize(writer, data);

        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak");
        else
            File.Move(tmp, path);
    }
}
