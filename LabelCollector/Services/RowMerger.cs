using LabelCollector.Models;

namespace LabelCollector.Services;

public record MergeResult(List<RowItem> Rows, int Added, int Updated, int Missing, int Restored);

/// <summary>
/// データ元の最新内容と既存の行をキーで照合してマージする。
/// ・同じキーの行 → A列だけ更新し、B列はそのまま
/// ・新しいキー   → 行を追加（B列は空）
/// ・消えたキー   → 削除せず Missing にして B列を保持（再び現れたら自動で復活）
/// </summary>
public static class RowMerger
{
    public static MergeResult Merge(IEnumerable<RowItem> existing, IEnumerable<SourceRecord> source, DateTime now)
    {
        // キー → 既存行（キーが重複していた場合は先勝ち）
        var byKey = new Dictionary<string, RowItem>(StringComparer.OrdinalIgnoreCase);
        var existingList = existing.ToList();
        foreach (var row in existingList)
            byKey.TryAdd(row.Key, row);

        var result = new List<RowItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0, restored = 0;

        // データ元の並び順で行を並べる
        foreach (var rec in source)
        {
            if (!seen.Add(rec.Key)) continue; // データ元側の重複は無視

            if (byKey.TryGetValue(rec.Key, out var row))
            {
                if (row.State == RowState.Missing) restored++;
                if (row.ColumnA != rec.ValueA) updated++;
                row.ColumnA = rec.ValueA;
                row.State = RowState.Normal;
                row.LastSeen = now;
                result.Add(row);
            }
            else
            {
                result.Add(new RowItem
                {
                    Key = rec.Key,
                    ColumnA = rec.ValueA,
                    ColumnB = "",
                    State = RowState.New,
                    LastSeen = now,
                });
                added++;
            }
        }

        // データ元から消えた行は末尾に残す
        int missing = 0;
        var kept = new HashSet<RowItem>(result, ReferenceEqualityComparer.Instance);
        foreach (var row in existingList)
        {
            if (kept.Contains(row)) continue;
            row.State = RowState.Missing;
            result.Add(row);
            missing++;
        }

        return new MergeResult(result, added, updated, missing, restored);
    }
}
