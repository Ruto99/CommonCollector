using System.Xml.Serialization;

namespace LabelCollector.Models.Sources;

/// <summary>
/// フォルダ内のファイルの一覧を読むデータ元。キー＝フォルダからの相対パス。
/// 取り込める列：ファイル名・拡張子・フォルダ・更新日時・サイズ
/// </summary>
[XmlType("FileList")]
public class FileListSourceSettings : SourceSettings, IFolderLocation
{
    private string _folderPath = "";
    private string _filePattern = "*.*";
    private bool _includeSubfolders = true;

    public string FolderPath { get => _folderPath; set => Set(ref _folderPath, value); }

    /// <summary>対象にするファイル（例：*.pdf）</summary>
    public string FilePattern { get => _filePattern; set => Set(ref _filePattern, value); }

    public bool IncludeSubfolders { get => _includeSubfolders; set => Set(ref _includeSubfolders, value); }

    [XmlIgnore]
    public override string Location => FolderPath;
}
