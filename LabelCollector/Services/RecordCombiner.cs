using LabelCollector.Models;

namespace LabelCollector.Services;

/// <summary>
/// 複数のデータ元から読んだ行を、キーで1つにまとめる。
/// ・別のデータ元に同じキーがあれば、1行にまとめて列を足し合わせる（同じ列は先のデータ元の値を使う）
/// ・同じデータ元の中で同じキーが重複していたら、先のものを使い、重複として報告する
/// </summary>
public class RecordCombiner
{
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _byKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>同じデータ元の中で重複したキー（「キー (取得元, 取得元)」の形）</summary>
    public List<string> Duplicates { get; } = new();

    private class Entry(string key)
    {
        public string Key { get; } = key;
        public List<string> Origins { get; } = new();
        public Dictionary<string, string> Values { get; } = new();
    }

    /// <summary>1つのデータ元から読んだ行を足す</summary>
    public void Add(IEnumerable<SourceRecord> records)
    {
        var inThisSource = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in records)
        {
            if (inThisSource.TryGetValue(rec.Key, out var origins))
            {
                origins.Add(rec.Origin);
                continue; // 同じデータ元の重複は先のものを使う
            }
            inThisSource[rec.Key] = [rec.Origin];

            if (!_byKey.TryGetValue(rec.Key, out var entry))
            {
                entry = new Entry(rec.Key);
                _byKey[rec.Key] = entry;
                _entries.Add(entry);
            }
            if (!entry.Origins.Contains(rec.Origin)) entry.Origins.Add(rec.Origin);
            foreach (var (column, value) in rec.Values)
            {
                // 同じ列は先に読んだデータ元を優先。ただし空なら後の値で埋める
                if (!entry.Values.TryGetValue(column, out var current) || current.Length == 0)
                    entry.Values[column] = value;
            }
        }

        Duplicates.AddRange(inThisSource.Where(kv => kv.Value.Count > 1)
                                        .Select(kv => $"{kv.Key} ({string.Join(", ", kv.Value)})"));
    }

    /// <summary>まとめた結果（最初に現れた順）</summary>
    public List<SourceRecord> ToRecords() =>
        _entries.Select(e => new SourceRecord(e.Key, string.Join(" ; ", e.Origins), e.Values)).ToList();
}
