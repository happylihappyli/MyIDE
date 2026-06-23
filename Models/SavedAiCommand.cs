using System;
using System.Text.Json.Serialization;

namespace MyIDE.Models;

/// <summary>
/// 持久化保存到右侧收藏区的 AI 命令。
/// </summary>
public class SavedAiCommand
{
    /// <summary>命令唯一标识。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>所属项目根目录。</summary>
    [JsonPropertyName("projectRoot")]
    public string ProjectRoot { get; set; } = "";

    /// <summary>命令名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>命令用途说明。</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    /// <summary>命令文本。</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>命令运行的 shell。</summary>
    [JsonPropertyName("shell")]
    public string Shell { get; set; } = "powershell";

    /// <summary>相对项目根目录的工作目录。</summary>
    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    /// <summary>是否为可选命令。</summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; }

    /// <summary>是否设为默认编译命令。</summary>
    [JsonPropertyName("isDefaultBuild")]
    public bool IsDefaultBuild { get; set; }

    /// <summary>是否设为默认运行命令。</summary>
    [JsonPropertyName("isDefaultRun")]
    public bool IsDefaultRun { get; set; }

    /// <summary>最近更新时间，便于排序。</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>同一项目下的自定义排序值，越小越靠前。</summary>
    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    /// <summary>
    /// 生成列表中更易读的显示文本。
    /// </summary>
    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var prefix = "";
            if (IsDefaultBuild) prefix += "[编译默认]";
            if (IsDefaultRun) prefix += "[运行默认]";

            var title = string.IsNullOrWhiteSpace(Name) ? Command : Name;
            if (title.Length > 40) title = title[..40] + "...";
            return string.IsNullOrWhiteSpace(prefix) ? title : $"{prefix} {title}";
        }
    }
}
