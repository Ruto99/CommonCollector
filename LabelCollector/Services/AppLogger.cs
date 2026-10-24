using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;

namespace LabelCollector.Services;

public enum LogLevel
{
    Info,
    Warn,
    Error,
}

public record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss.fff");
    public string LevelText => Level switch
    {
        LogLevel.Warn => "警告",
        LogLevel.Error => "エラー",
        _ => "情報",
    };
}

/// <summary>
/// 画面のログ欄に出しつつ、exe と同じフォルダの logs\ にも日付ごとのファイルで保存する。
/// どのスレッドから呼んでもよい。
/// </summary>
public sealed class AppLogger
{
    private const int MaxEntries = 2000;
    private readonly object _fileLock = new();

    public static AppLogger Instance { get; } = new();

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public string LogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "logs");

    private AppLogger() { }

    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message) => Write(LogLevel.Error, message);

    public void Clear()
    {
        RunOnUi(Entries.Clear);
    }

    private void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);

        RunOnUi(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        });

        WriteToFile(entry);
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private void WriteToFile(LogEntry e)
    {
        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, $"LabelCollector_{e.Time:yyyyMMdd}.log");
                var line = $"{e.Time:yyyy-MM-dd HH:mm:ss.fff} [{e.Level.ToString().ToUpperInvariant(),-5}] {e.Message}{Environment.NewLine}";
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // ログの書き込み失敗でアプリを止めない
        }
    }
}
