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
