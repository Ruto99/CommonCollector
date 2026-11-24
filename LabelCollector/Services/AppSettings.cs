using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace LabelCollector.Services;

/// <summary>アプリの設定（最後に使った中間データ(.lc)の場所、作業フォルダ）。exe と同じフォルダの settings.xml に保存</summary>
public class AppSettings
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "settings.xml");
    private static readonly XmlSerializer Serializer = new(typeof(AppSettings));

    public string WorkspacePath { get; set; } = "";

    /// <summary>左のエクスプローラーに表示する作業フォルダ</summary>
    public string WorkDirectory { get; set; } = "";

    /// <summary>
    /// 組にして一緒に入力する、2つの手入力の列の名前（どのシートでも同じ）。
    /// シートにこの2列があると、値を「1つ目の列の値と、それに紐づく2つ目の列の値」の組で入力する。
    /// 名前は settings.xml で変えられる（空にすると組にしない）
    /// </summary>
    public string LinkedColumn1 { get; set; } = "部品番号";
    public string LinkedColumn2 { get; set; } = "数量";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var s = File.OpenRead(FilePath);
                if (Serializer.Deserialize(s) is AppSettings a) return a;
            }
        }
        catch
        {
            // 壊れていたら初期値で続行
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            using var w = new StreamWriter(FilePath, false, new UTF8Encoding(false));
            Serializer.Serialize(w, this);
        }
        catch
        {
            // 設定が保存できなくても動作は続ける
        }
    }
}
