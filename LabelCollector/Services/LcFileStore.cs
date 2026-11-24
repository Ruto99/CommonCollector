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

    /// <summary>
    /// データ元の設定クラスは DataSourceRegistry に登録されたものを使う（xsi:type で種類を区別して保存する）。
    /// 登録が済んでから作るよう、初めて使うときに作る
    /// </summary>
    private static readonly Lazy<XmlSerializer> Serializer =
        new(() => new XmlSerializer(typeof(TableData), DataSourceRegistry.SettingsTypes));

    public static TableData Load(string path)
    {
        if (!File.Exists(path)) return new TableData();
        using var stream = File.OpenRead(path);
        var data = (TableData?)Serializer.Value.Deserialize(stream) ?? new TableData();
        data.MigrateLegacy(); // 以前の形式なら今の形式に直す
        return data;
    }

    /// <summary>中身を XML の文字列にする（元に戻す・やり直しの履歴用）</summary>
    public static string ToXml(TableData data)
    {
        using var sw = new StringWriter();
        using (var writer = XmlWriter.Create(sw, new XmlWriterSettings { Indent = false }))
            Serializer.Value.Serialize(writer, data);
        return sw.ToString();
    }

    /// <summary>ToXml で作った文字列から読み直す</summary>
    public static TableData FromXml(string xml)
    {
        using var sr = new StringReader(xml);
        return (TableData?)Serializer.Value.Deserialize(sr) ?? new TableData();
    }

    /// <summary>データ元の設定を比べるための文字列</summary>
    public static string SourcesXml(IEnumerable<SourceSettings> sources) =>
        ToXml(new TableData { Sheets = { new SheetData { Sources = new(sources) } } });

    public static void Save(string path, TableData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // 一時ファイルに書いてから置き換える（保存中に落ちても元のファイルが壊れない）
        var tmp = path + ".tmp";
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var writer = XmlWriter.Create(tmp, settings))
            Serializer.Value.Serialize(writer, data);

        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak");
        else
            File.Move(tmp, path);
    }
}
