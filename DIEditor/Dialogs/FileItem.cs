using System;

namespace DIEditor.Dialogs;

/// <summary>
/// 锁定目录对话框中的单个文件条目。只承载展示所需的快照数据，
/// 不在 getter 里做任何 IO，保证 ListView 虚拟化不被破坏。
/// </summary>
public sealed class FileItem
{
    public string Name { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;

    public long Size { get; init; }

    public DateTime ModifiedTime { get; init; }

    /// <summary>右侧次要信息：大小 + 修改时间。</summary>
    public string DetailText => $"{FormatSize(Size)} · {ModifiedTime:yyyy-MM-dd HH:mm}";

    /// <summary>按扩展名映射的 Segoe Fluent Icons 字形，避免加载真实缩略图。</summary>
    public string IconGlyph => Extension switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".svg" => "\uEB9F",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" => "\uE714",
        ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "\uE8D6",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "\uE7B8",
        ".json" or ".xml" or ".yaml" or ".yml" or ".ini" or ".config" or ".toml" => "\uE943",
        ".cs" or ".cpp" or ".h" or ".py" or ".js" or ".ts" or ".xaml" => "\uE943",
        ".exe" or ".dll" => "\uE7B8",
        _ => "\uE8A5",
    };

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        double size = bytes;
        var index = -1;

        do
        {
            size /= 1024;
            index++;
        }
        while (size >= 1024 && index < units.Length - 1);

        return $"{size:0.#} {units[index]}";
    }
}
