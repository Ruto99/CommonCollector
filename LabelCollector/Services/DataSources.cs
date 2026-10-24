using System.IO;
using Microsoft.Data.Sqlite;
using LabelCollector.Models;

namespace LabelCollector.Services;

/// <summary>A列の値を集めてくるデータ元</summary>
public interface IDataSource
{
    List<SourceRecord> Fetch();
}

/// <summary>フォルダ内のファイルを集める。キー＝相対パス、A列＝ファイル名</summary>
public class FolderDataSource(string folder, string pattern) : IDataSource
{
    public List<SourceRecord> Fetch()
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {folder}");

        return Directory
            .EnumerateFiles(folder, string.IsNullOrWhiteSpace(pattern) ? "*.*" : pattern, SearchOption.AllDirectories)
            .Select(f => new SourceRecord(Path.GetRelativePath(folder, f), Path.GetFileName(f)))
            .OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// ローカルDB（SQLite）から集める。SQLの1列目＝キー、2列目＝A列（2列目が無ければ1列目をA列にも使う）
/// </summary>
public class SqliteDataSource(string dbPath, string query) : IDataSource
{
    public List<SourceRecord> Fetch()
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"DBファイルが見つかりません: {dbPath}");

        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();

        var list = new List<SourceRecord>();
        while (reader.Read())
        {
            var key = Convert.ToString(reader.GetValue(0)) ?? "";
            var a = reader.FieldCount > 1 ? Convert.ToString(reader.GetValue(1)) ?? "" : key;
            list.Add(new SourceRecord(key, a));
        }
        return list;
    }
}
