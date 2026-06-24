using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MyIDE.Models;

/// <summary>
/// AI 返回的整体修改方案（对应提示词中约定的 JSON 规范）
/// </summary>
public class ChangePlan
{
    /// <summary>任务简要说明（AI 自述这次做了什么）</summary>
    [JsonPropertyName("task")]
    public string Task { get; set; } = "";

    /// <summary>需要修改的文件列表</summary>
    [JsonPropertyName("changes")]
    public List<FileChange> Changes { get; set; } = new();

    /// <summary>应用修改后可执行的命令列表</summary>
    [JsonPropertyName("commands")]
    public List<AiCommand> Commands { get; set; } = new();
}

/// <summary>
/// 单个文件的修改方案
/// </summary>
public class FileChange
{
    /// <summary>相对项目根目录的路径，例如 src/main.cpp</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    /// <summary>对该文件的一系列操作（按顺序执行）</summary>
    [JsonPropertyName("ops")]
    public List<LineOp> Ops { get; set; } = new();

    /// <summary>AI 可能直接返回 unified diff 格式的代码（非协议内，但提供兼容处理）</summary>
    [JsonPropertyName("diff")]
    public string Diff { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

/// <summary>
/// 对文件的一行级操作
/// </summary>
public class LineOp
{
    /// <summary>操作类型：replace / insert / delete</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "replace";

    /// <summary>起始行（1 开始，包含）。replace/delete 时使用</summary>
    [JsonPropertyName("start")]
    public int? Start { get; set; }

    /// <summary>结束行（1 开始，包含）。replace/delete 时使用</summary>
    [JsonPropertyName("end")]
    public int? End { get; set; }

    /// <summary>在第 N 行之后插入。insert 时使用</summary>
    [JsonPropertyName("after")]
    public int? After { get; set; }

    /// <summary>新内容（多行用 \n）</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>原内容，用于 Diff 模式下的精准匹配</summary>
    [JsonPropertyName("oldContent")]
    public string? OldContent { get; set; }
}

/// <summary>
/// AI 返回的可执行命令描述。
/// </summary>
public class AiCommand
{
    /// <summary>命令名称，便于在 UI 中阅读</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>命令用途说明</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    /// <summary>AI 有时返回 description 而非 reason</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>命令文本本身</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    /// <summary>执行所用 shell，Windows 下默认 powershell</summary>
    [JsonPropertyName("shell")]
    public string Shell { get; set; } = "powershell";

    /// <summary>相对项目根目录的工作目录，空字符串表示项目根目录</summary>
    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    /// <summary>是否为可选命令</summary>
    [JsonPropertyName("optional")]
    public bool Optional { get; set; }
}
