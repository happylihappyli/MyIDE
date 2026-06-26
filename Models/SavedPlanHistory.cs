using System;
using System.Text.Json.Serialization;

namespace MyIDE.Models;

/// <summary>
/// 持久化保存的计划历史记录。
/// </summary>
public class SavedPlanHistory
{
    /// <summary>记录唯一标识。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>所属项目根目录。</summary>
    [JsonPropertyName("projectRoot")]
    public string ProjectRoot { get; set; } = "";

    /// <summary>计划标题，通常取首行摘要。</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>完整计划文本。</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

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
