using System.Xml.Serialization;

namespace LabelCollector.Models.Sources;

/// <summary>エクセルファイル 1つを読むデータ元</summary>
[XmlType("ExcelFile")]
public class ExcelFileSourceSettings : TableSourceSettings, IFileLocation
{
    private string _filePath = "";

    public string FilePath { get => _filePath; set => Set(ref _filePath, value); }

    [XmlIgnore]
    public string FileDialogFilter => "エクセル (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|すべてのファイル (*.*)|*.*";

    [XmlIgnore]
    public override string Location => FilePath;
}

/// <summary>フォルダ内のエクセルファイルをまとめて読むデータ元</summary>
[XmlType("ExcelFolder")]
public class ExcelFolderSourceSettings : TableSourceSettings, IFolderLocation
{
    private string _folderPath = "";
    private bool _includeSubfolders = true;

    public string FolderPath { get => _folderPath; set => Set(ref _folderPath, value); }

    /// <summary>サブフォルダのファイルも読むか</summary>
    public bool IncludeSubfolders { get => _includeSubfolders; set => Set(ref _includeSubfolders, value); }

    [XmlIgnore]
    public override string Location => FolderPath;
}
