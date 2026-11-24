namespace LabelCollector.Models;

/// <summary>
/// 組にして一緒に入力する、2つの手入力の列（例：部品番号と、それに紐づく数量）。どのシートでも同じ名前。
/// 名前はアプリの設定（settings.xml の LinkedColumn1 / LinkedColumn2）から、起動時に入れる。
/// シートにこの2列がそろっているときだけ、値を「1つ目の列の値と2つ目の列の値」の組で入力する
/// </summary>
public static class LinkedColumns
{
    /// <summary>1つ目の列（組の見出しになる値。例：部品番号）</summary>
    public static string First { get; set; } = "";

    /// <summary>2つ目の列（1つ目の値に紐づく値。例：数量）</summary>
    public static string Second { get; set; } = "";

    /// <summary>組の設定があるか（2つの名前が入っていて、違う名前）</summary>
    public static bool IsEnabled => First.Length > 0 && Second.Length > 0 && First != Second;

    /// <summary>手入力の列の名前の一覧に、組の2列がそろっているか</summary>
    public static bool IsPresentIn(IEnumerable<string> manualColumns)
    {
        if (!IsEnabled) return false;
        var names = manualColumns.ToHashSet();
        return names.Contains(First) && names.Contains(Second);
    }

    /// <summary>組の列か</summary>
    public static bool Contains(string column) => IsEnabled && (column == First || column == Second);
}
