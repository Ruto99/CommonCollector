using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using LabelCollector.Models;

namespace LabelCollector.Services;

/// <summary>中間データを XML で読み書きする</summary>
public static class XmlStore
{
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

        // 一時ファイルに書いてから置き換える（保存中に落ちても元のXMLが壊れない）
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
