using System.Windows;

namespace LabelCollector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 独自のデータ元を追加するときは、ここ（中間データを読む前）で登録する。
        // 組み込みのもの（エクセルファイル・エクセルフォルダ・ファイル一覧）は DataSourceRegistry に登録済み。
        // 例：
        // Services.DataSourceRegistry.Register<CsvSourceSettings>("CSVファイル", "🧾",
        //     () => new CsvSourceSettings(), s => new CsvReader(s));
        // 詳しくは docs/データ元の追加方法.md を参照。
        base.OnStartup(e);
    }
}
