using LabelCollector.Models;
using LabelCollector.Services;

// 1回目: a, b, c
var rows = RowMerger.Merge([], [new("a","A1"), new("b","B1"), new("c","C1")], DateTime.Now).Rows;
rows.First(r => r.Key == "b").ColumnB = "メモb";
rows.First(r => r.Key == "c").ColumnB = "メモc";

// 2回目: 先頭に x を追加し、b を削除（位置で管理していたら B列がずれるケース）
var r2 = RowMerger.Merge(rows, [new("x","X1"), new("a","A1"), new("c","C2")], DateTime.Now);
Dump("2回目", r2.Rows);

// XMLに保存→読み込み
var path = Path.Combine(Path.GetTempPath(), "mergecheck.xml");
XmlStore.Save(path, new TableData { Sheets = { new SheetData { Name = "t", Rows = r2.Rows } } });
var loaded = XmlStore.Load(path).Sheets[0].Rows;

// 3回目: b が戻ってくる
var r3 = RowMerger.Merge(loaded, [new("a","A1"), new("b","B1"), new("c","C2")], DateTime.Now);
Dump("3回目", r3.Rows);

bool ok = r3.Rows.First(r => r.Key == "b").ColumnB == "メモb"
       && r3.Rows.First(r => r.Key == "c").ColumnB == "メモc"
       && r3.Rows.First(r => r.Key == "c").ColumnA == "C2";
Console.WriteLine(ok ? "OK" : "NG");
Console.WriteLine(File.ReadAllText(path));

static void Dump(string t, List<RowItem> rs)
{
    Console.WriteLine($"--- {t}");
    foreach (var r in rs) Console.WriteLine($"{r.Key,-3} A={r.ColumnA,-4} B={r.ColumnB,-6} {r.State}");
}
