using System;
using System.Text.Json.Serialization;

namespace MyIDE.Models;

/// <summary>
/// 持久化保存的 AI 返回 JSON 记录。
/// </summary>
public class SavedAiJson
{
    /// <summary>记录唯一标识。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>所属项目根目录。</summary>
    [JsonPropertyName("projectRoot")]
    public string ProjectRoot { get; set; } = "";

    /// <summary>AI 返回任务摘要。</summary>
    [JsonPropertyName("task")]
    public string Task { get; set; } = "";

    /// <summary>原始 JSON 文本。</summary>
    [JsonPropertyName("jsonText")]
    public string JsonText { get; set; } = "";

    /// <summary>修改文件数。</summary>
    [JsonPropertyName("changeCount")]
    public int ChangeCount { get; set; }

    /// <summary>命令数。</summary>
    [JsonPropertyName("commandCount")]
    public int CommandCount { get; set; }

    /// <summary>最近更新时间。</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>同一项目下的排序值。</summary>
    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }

    /// <summary>是否置顶显示。</summary>
    [JsonPropertyName("isPinned")]
    public bool IsPinned { get; set; }
}
