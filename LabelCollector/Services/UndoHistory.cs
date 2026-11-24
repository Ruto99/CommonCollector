namespace LabelCollector.Services;

/// <summary>
/// 元に戻す・やり直しの履歴。中間データの中身（XML の文字列）を、変更のたびに丸ごと覚えておく。
/// Current は今の状態、元に戻すと1つ前の状態が Current になる
/// </summary>
public class UndoHistory(int limit = 100)
{
    private readonly List<string> _undo = new(); // 古い順
    private readonly List<string> _redo = new(); // 新しい順の逆（最後が次にやり直すもの）

    /// <summary>今の状態</summary>
    public string? Current { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>履歴を消して、今の状態だけにする（中間データを開いたときなど）</summary>
    public void Reset(string state)
    {
        _undo.Clear();
        _redo.Clear();
        Current = state;
    }

    /// <summary>変更後の状態を記録する。前と同じなら何もしない。やり直しの履歴は消える</summary>
    public bool Record(string state)
    {
        if (Current == null)
        {
            Current = state;
            return false;
        }
        if (state == Current) return false;

        _undo.Add(Current);
        if (_undo.Count > limit) _undo.RemoveAt(0);
        _redo.Clear();
        Current = state;
        return true;
    }

    /// <summary>1つ前の状態に戻す。戻す先が無ければ null</summary>
    public string? Undo()
    {
        if (!CanUndo || Current == null) return null;
        _redo.Add(Current);
        Current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return Current;
    }

    /// <summary>元に戻したものをやり直す。やり直す先が無ければ null</summary>
    public string? Redo()
    {
        if (!CanRedo || Current == null) return null;
        _undo.Add(Current);
        Current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return Current;
    }

    /// <summary>戻した状態を画面に反映した後の、実際の状態に置き換える（履歴は変えない）</summary>
    public void ReplaceCurrent(string state) => Current = state;
}
