using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using MyIDE.Models;

namespace MyIDE.Services;

/// <summary>
/// 修改应用器：把 AI 返回的 JSON 修改方案真实地落到磁盘上
/// </summary>
public class ChangeApplier
{
    /// <summary>
    /// 单条操作的应用结果
    /// </summary>
    public class ApplyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 整体应用结果汇总
    /// </summary>
    public class ApplySummary
    {
        public int TotalFiles { get; set; }
        public int TotalOps { get; set; }
        public int SuccessOps { get; set; }
        public List<string> Log { get; set; } = new();

        public override string ToString()
            => $"文件 {TotalFiles} 个，操作 {SuccessOps}/{TotalOps} 成功\n\n" +
               string.Join("\n", Log);
    }

    private readonly string _projectRoot;

    public ChangeApplier(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    private const string PatchBeginMarker = "*** Begin Patch";
    private const string PatchEndMarker = "*** End Patch";

    /// <summary>
    /// 解析 AI 返回的文本，优先支持 Patch 协议，同时兼容旧 JSON 协议。
    /// </summary>
    public ChangePlan ParseJson(string rawText)
    {
        if (LooksLikePatchResponse(rawText))
        {
            return ParsePatchResponse(rawText);
        }

        return ParseLegacyJson(rawText);
    }

    /// <summary>
    /// 解析旧版 JSON 响应文本（容忍 ```json 包裹、容忍前后杂讯）。
    /// </summary>
    private ChangePlan ParseLegacyJson(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            throw new InvalidDataException("AI 返回内容为空");

        var text = rawText.Trim();

        // 去掉 ```json ... ``` 之类的 Markdown 包裹
        if (text.StartsWith("```"))
        {
            int firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd > 0)
            {
                text = text.Substring(firstLineEnd + 1);
            }
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0) text = text.Substring(0, lastFence);
            text = text.Trim();
        }

        // 尝试提取第一个 { 到最后一个 }
        int first = text.IndexOf('{');
        int last = text.LastIndexOf('}');
        if (first >= 0 && last > first)
            text = text.Substring(first, last - first + 1);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        var plan = JsonSerializer.Deserialize<ChangePlan>(text, options)
            ?? throw new InvalidDataException("JSON 解析结果为空");
            
        PostProcessParsedPlan(plan);
        return plan;
    }

    /// <summary>
    /// 判断文本是否更像 Patch 协议响应。
    /// </summary>
    private static bool LooksLikePatchResponse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return false;

        var text = rawText.Trim();
        return text.Contains(PatchBeginMarker, StringComparison.Ordinal) ||
               text.StartsWith("```patch", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("【补丁】", StringComparison.Ordinal) ||
               text.Contains("*** Add File:", StringComparison.Ordinal) ||
               text.Contains("*** Update File:", StringComparison.Ordinal);
    }

    /// <summary>
    /// 解析新的 Patch 协议响应，支持【任务】、【补丁】和【命令】三个区段。
    /// </summary>
    private ChangePlan ParsePatchResponse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            throw new InvalidDataException("AI 返回内容为空");

        var normalized = NormalizeLineEndings(rawText).Trim();
        var patchText = ExtractPatchText(normalized);
        var plan = new ChangePlan
        {
            Task = ExtractSectionText(normalized, "【任务】", new[] { "【补丁】", "【命令】" })
        };
        if (string.IsNullOrWhiteSpace(plan.Task))
        {
            plan.Task = "应用 AI Patch 修改";
        }

        plan.Commands = ParseCommandsSection(normalized);
        plan.Changes = string.IsNullOrWhiteSpace(patchText)
            ? new List<FileChange>()
            : ParsePatchChanges(patchText);
        if (plan.Changes.Count == 0 && plan.Commands.Count == 0)
            throw new InvalidDataException("Patch 响应中没有识别到修改或命令");
        PostProcessParsedPlan(plan);
        return plan;
    }

    /// <summary>
    /// 提取命令区，允许为空；命令内容继续使用 JSON 数组，降低额外解析复杂度。
    /// </summary>
    private List<AiCommand> ParseCommandsSection(string normalizedText)
    {
        var commandsText = ExtractSectionText(normalizedText, "【命令】", Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(commandsText))
        {
            return new List<AiCommand>();
        }

        var extracted = ExtractJsonArray(commandsText);
        if (string.IsNullOrWhiteSpace(extracted))
        {
            return new List<AiCommand>();
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        return JsonSerializer.Deserialize<List<AiCommand>>(extracted, options) ?? new List<AiCommand>();
    }

    /// <summary>
    /// 解析 Patch 中的文件修改列表。
    /// </summary>
    private List<FileChange> ParsePatchChanges(string patchText)
    {
        var lines = NormalizeLineEndings(patchText).Split('\n');
        var changes = new List<FileChange>();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.StartsWith(PatchBeginMarker, StringComparison.Ordinal) ||
                line.StartsWith(PatchEndMarker, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (line.StartsWith("*** Add File: ", StringComparison.Ordinal))
            {
                var filePath = line["*** Add File: ".Length..].Trim();
                i++;
                var newFileLines = new List<string>();
                while (i < lines.Length && !lines[i].StartsWith("*** ", StringComparison.Ordinal))
                {
                    var contentLine = lines[i];
                    if (contentLine.StartsWith("+", StringComparison.Ordinal))
                    {
                        newFileLines.Add(contentLine[1..]);
                    }
                    i++;
                }

                changes.Add(new FileChange
                {
                    File = NormalizePatchPath(filePath),
                    Description = "Patch 新建文件",
                    Ops = new List<LineOp>
                    {
                        new()
                        {
                            Type = "insert",
                            After = 0,
                            Content = string.Join("\n", newFileLines)
                        }
                    }
                });
                continue;
            }

            if (line.StartsWith("*** Update File: ", StringComparison.Ordinal))
            {
                var filePath = line["*** Update File: ".Length..].Trim();
                i++;
                var fileChange = new FileChange
                {
                    File = NormalizePatchPath(filePath),
                    Description = "Patch 更新文件"
                };

                while (i < lines.Length && !lines[i].StartsWith("*** ", StringComparison.Ordinal))
                {
                    if (!lines[i].StartsWith("@@", StringComparison.Ordinal))
                    {
                        i++;
                        continue;
                    }

                    i++;
                    var hunkLines = new List<string>();
                    while (i < lines.Length &&
                           !lines[i].StartsWith("@@", StringComparison.Ordinal) &&
                           !lines[i].StartsWith("*** ", StringComparison.Ordinal))
                    {
                        if (lines[i] == "*** End of File")
                        {
                            i++;
                            break;
                        }

                        hunkLines.Add(lines[i]);
                        i++;
                    }

                    var op = ConvertPatchHunkToOp(hunkLines);
                    if (op != null)
                    {
                        fileChange.Ops.Add(op);
                    }
                }

                if (fileChange.Ops.Count > 0)
                {
                    changes.Add(fileChange);
                }
                continue;
            }

            i++;
        }

        return changes;
    }

    /// <summary>
    /// 把单个 patch hunk 转成 replace 操作，后续由旧的模拟/应用逻辑继续处理。
    /// </summary>
    private static LineOp? ConvertPatchHunkToOp(List<string> hunkLines)
    {
        if (hunkLines.Count == 0) return null;

        var oldLines = new List<string>();
        var newLines = new List<string>();

        foreach (var line in hunkLines)
        {
            if (line.Length == 0)
            {
                oldLines.Add("");
                newLines.Add("");
                continue;
            }

            if (line.StartsWith(" ", StringComparison.Ordinal))
            {
                var content = line[1..];
                oldLines.Add(content);
                newLines.Add(content);
                continue;
            }

            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                oldLines.Add(line[1..]);
                continue;
            }

            if (line.StartsWith("+", StringComparison.Ordinal))
            {
                newLines.Add(line[1..]);
            }
        }

        if (oldLines.Count == 0)
        {
            return new LineOp
            {
                Type = "insert",
                After = 0,
                Content = string.Join("\n", newLines)
            };
        }

        return new LineOp
        {
            Type = "replace",
            Start = 1,
            End = Math.Max(1, oldLines.Count),
            Content = string.Join("\n", newLines),
            OldContent = string.Join("\n", oldLines)
        };
    }

    /// <summary>
    /// 提取 patch 区块，支持纯 patch、带【补丁】标题和 ```patch 包裹三种形式。
    /// </summary>
    private static string ExtractPatchText(string normalizedText)
    {
        var beginIndex = normalizedText.IndexOf(PatchBeginMarker, StringComparison.Ordinal);
        var endIndex = normalizedText.LastIndexOf(PatchEndMarker, StringComparison.Ordinal);
        if (beginIndex >= 0 && endIndex > beginIndex)
        {
            return normalizedText.Substring(beginIndex, endIndex - beginIndex + PatchEndMarker.Length);
        }

        var fencedMatch = Regex.Match(
            normalizedText,
            @"```patch\s*(?<body>[\s\S]*?)```",
            RegexOptions.IgnoreCase);
        if (fencedMatch.Success)
        {
            return fencedMatch.Groups["body"].Value.Trim();
        }

        if (normalizedText.Contains("【命令】", StringComparison.Ordinal))
        {
            return "";
        }

        return normalizedText;
    }

    /// <summary>
    /// 提取形如【任务】/【补丁】/【命令】的区段文本。
    /// </summary>
    private static string ExtractSectionText(string text, string sectionMarker, string[] nextMarkers)
    {
        var startIndex = text.IndexOf(sectionMarker, StringComparison.Ordinal);
        if (startIndex < 0) return "";

        startIndex += sectionMarker.Length;
        var remaining = text[startIndex..];
        var endIndex = remaining.Length;
        foreach (var next in nextMarkers)
        {
            if (string.IsNullOrWhiteSpace(next)) continue;
            var markerIndex = remaining.IndexOf(next, StringComparison.Ordinal);
            if (markerIndex >= 0 && markerIndex < endIndex)
            {
                endIndex = markerIndex;
            }
        }

        return remaining[..endIndex].Trim();
    }

    /// <summary>
    /// 从命令区文本里抽出 JSON 数组，兼容 ```json 包裹。
    /// </summary>
    private static string ExtractJsonArray(string text)
    {
        var trimmed = text.Trim();
        var fencedMatch = Regex.Match(
            trimmed,
            @"```json\s*(?<body>[\s\S]*?)```",
            RegexOptions.IgnoreCase);
        if (fencedMatch.Success)
        {
            trimmed = fencedMatch.Groups["body"].Value.Trim();
        }

        var first = trimmed.IndexOf('[');
        var last = trimmed.LastIndexOf(']');
        if (first >= 0 && last > first)
        {
            return trimmed.Substring(first, last - first + 1);
        }

        return trimmed;
    }

    /// <summary>
    /// 把 patch 里的文件路径统一规范化为项目相对路径。
    /// </summary>
    private static string NormalizePatchPath(string filePath)
    {
        return (filePath ?? "")
            .Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// 统一文本换行符，方便 patch 和区段解析。
    /// </summary>
    private static string NormalizeLineEndings(string text)
    {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
    }

    private void PostProcessParsedPlan(ChangePlan plan)
    {
        foreach (var cmd in plan.Commands)
        {
            if (string.IsNullOrWhiteSpace(cmd.Name) && !string.IsNullOrWhiteSpace(cmd.Description))
                cmd.Name = cmd.Description;
            if (string.IsNullOrWhiteSpace(cmd.Reason) && !string.IsNullOrWhiteSpace(cmd.Description))
                cmd.Reason = cmd.Description;
        }

        foreach (var fc in plan.Changes)
        {
            if (fc.Ops.Count == 0 && !string.IsNullOrWhiteSpace(fc.Diff))
            {
                fc.Ops = ConvertDiffToOps(fc.Diff);
            }
        }
    }

    private List<LineOp> ConvertDiffToOps(string diff)
    {
        var ops = new List<LineOp>();
        var lines = diff.Replace("\r\n", "\n").Split('\n');
        
        int i = 0;
        while (i < lines.Length)
        {
            if (lines[i].StartsWith("@@ "))
            {
                var header = lines[i];
                var parts = header.Split(' ');
                if (parts.Length >= 3 && parts[1].StartsWith("-"))
                {
                    var oldRange = parts[1].Substring(1).Split(',');
                    if (oldRange.Length >= 1 && int.TryParse(oldRange[0], out int startLine))
                    {
                        var newLines = new List<string>();
                        var oldLines = new List<string>();
                        int actualOldCount = 0;
                        i++;
                        
                        // Unified diff 可能会在修改前有一两行没有前缀的上下文
                        while (i < lines.Length && !lines[i].StartsWith("@@ ") && !lines[i].StartsWith("--- ") && !lines[i].StartsWith("+++ "))
                        {
                            var line = lines[i];
                            if (line.StartsWith("+"))
                            {
                                newLines.Add(line.Substring(1));
                            }
                            else if (line.StartsWith("-"))
                            {
                                oldLines.Add(line.Substring(1));
                                actualOldCount++;
                            }
                            else if (line.StartsWith(" "))
                            {
                                newLines.Add(line.Substring(1));
                                oldLines.Add(line.Substring(1));
                                actualOldCount++;
                            }
                            else if (line == "\\ No newline at end of file")
                            {
                                // 忽略
                            }
                            else if (string.IsNullOrWhiteSpace(line))
                            {
                                newLines.Add(""); // 空行作为上下文
                                oldLines.Add("");
                                actualOldCount++;
                            }
                            else
                            {
                                // 可能是非标准格式的上下文，保守当做原文件的一行
                                newLines.Add(line);
                                oldLines.Add(line);
                                actualOldCount++;
                            }
                            i++;
                        }
                        
                        // 如果计算出来的 actualOldCount 是 0（纯插入），避免 End < Start
                        int safeEnd = startLine + Math.Max(0, actualOldCount) - 1;
                        if (actualOldCount == 0) safeEnd = startLine - 1; // replace区间为空，相当于纯插入
                        
                        ops.Add(new LineOp
                        {
                            Type = "replace",
                            Start = startLine,
                            End = safeEnd,
                            Content = string.Join("\n", newLines),
                            OldContent = string.Join("\n", oldLines)
                        });
                        continue;
                    }
                }
            }
            i++;
        }
        return ops;
    }

    /// <summary>
    /// 单文件模拟结果
    /// </summary>
    public class SimulatedFile
    {
        public string RelativePath { get; set; } = "";
        public string OriginalContent { get; set; } = "";
        public string NewContent { get; set; } = "";
        public List<string> Issues { get; set; } = new();
    }

    /// <summary>
    /// 模拟应用：返回每个文件应用前/后的内容，不写磁盘，用于预览 diff
    /// </summary>
    public List<SimulatedFile> Simulate(ChangePlan plan)
    {
        var result = new List<SimulatedFile>();
        foreach (var fc in plan.Changes)
        {
            var sim = new SimulatedFile { RelativePath = fc.File };
            var fullPath = Path.Combine(_projectRoot, fc.File);
            List<string> lines;

            if (File.Exists(fullPath))
            {
                sim.OriginalContent = File.ReadAllText(fullPath);
                lines = sim.OriginalContent.Replace("\r\n", "\n").Split('\n').ToList();
                // 如果文件最后一行是空的（因为 \n 分割），为了行号准确，可以移除最后一个空字符串
                if (lines.Count > 0 && lines.Last() == "") lines.RemoveAt(lines.Count - 1);
            }
            else
            {
                sim.OriginalContent = "";
                lines = new List<string>();
                sim.Issues.Add("文件不存在，将创建新文件");
            }

            var ops = fc.Ops
                .Select(o => new
                {
                    Raw = o,
                    Start0 = (o.Start ?? 1) - 1,
                    End0 = (o.End ?? (o.Start ?? 1)) - 1,
                    After0 = (o.After ?? 0)
                })
                .OrderByDescending(x => OpBaseIndex(x.Raw, x.Start0, x.After0))
                .ToList();

            sim.Issues.Add($"[调试] 模拟对 {fc.File} 应用 {ops.Count} 个操作，当前文件行数: {lines.Count}");

            foreach (var op in ops)
            {
                var r = ApplyOneOp(lines, op.Raw, op.Start0, op.End0, op.After0);
                if (!r.Success)
                    sim.Issues.Add($"[{op.Raw.Type} 行 {op.Start0 + 1}-{op.End0 + 1}] 失败：{r.Message}");
                else
                    sim.Issues.Add($"[{op.Raw.Type} 行 {op.Start0 + 1}-{op.End0 + 1}] 成功：{r.Message}");
            }

            sim.NewContent = string.Join(Environment.NewLine, lines);
            if (sim.NewContent.Length > 0 && !sim.NewContent.EndsWith(Environment.NewLine)) sim.NewContent += Environment.NewLine;
            result.Add(sim);
        }
        return result;
    }

    /// <summary>
    /// 把整个 ChangePlan 应用到磁盘
    /// </summary>
    public ApplySummary Apply(ChangePlan plan, bool createBackup = true)
    {
        var summary = new ApplySummary { TotalFiles = plan.Changes.Count };
        foreach (var fc in plan.Changes)
        {
            summary.TotalOps += fc.Ops.Count;
            try
            {
                ApplyOneFile(fc, summary, createBackup);
            }
            catch (Exception ex)
            {
                summary.Log.Add($"[失败] {fc.File}：{ex.Message}");
            }
        }
        return summary;
    }

    private void ApplyOneFile(FileChange fc, ApplySummary summary, bool createBackup)
    {
        var fullPath = Path.Combine(_projectRoot, fc.File);
        bool isNewFile = !File.Exists(fullPath);
        List<string> lines;
        
        if (isNewFile)
        {
            lines = new List<string>();
            summary.Log.Add($"[创建] {fc.File}");
        }
        else
        {
            lines = File.ReadAllLines(fullPath).ToList();
        }

        var workingLines = new List<string>(lines);

        // 先计算并打印将要处理的 Ops，方便调试
        var ops = fc.Ops
            .Select(o => new
            {
                Raw = o,
                Start0 = (o.Start ?? 1) - 1,
                End0 = (o.End ?? (o.Start ?? 1)) - 1,
                After0 = (o.After ?? 0)
            })
            .OrderByDescending(x => OpBaseIndex(x.Raw, x.Start0, x.After0))
            .ToList();

        summary.Log.Add($"[调试] 准备对 {fc.File} 应用 {ops.Count} 个操作，当前文件行数: {workingLines.Count}");

        var localSuccessOps = 0;
        var localLogs = new List<string>();

        foreach (var op in ops)
        {
            var r = ApplyOneOp(workingLines, op.Raw, op.Start0, op.End0, op.After0);
            localLogs.Add($"[{fc.File}] {op.Raw.Type} -> {r.Message}");
            if (!r.Success)
            {
                summary.Log.AddRange(localLogs);
                summary.Log.Add($"[中止写盘] {fc.File}：存在未匹配或失败的补丁块，已放弃写入该文件");
                return;
            }

            localSuccessOps++;
        }

        // 只有真正准备写盘时才创建备份/目录，保证单文件应用具备事务性。
        if (!isNewFile)
        {
            if (createBackup)
            {
                try
                {
                    var backupPath = fullPath + ".bak";
                    File.Copy(fullPath, backupPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    summary.Log.Add($"[备份失败，忽略] {ex.Message}");
                }
            }
        }
        else
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                summary.Log.Add($"[创建目录] {dir}");
            }
        }

        // 写回文件（统一保证以换行符结尾）
        var newContent = string.Join(Environment.NewLine, workingLines);
        if (newContent.Length > 0 && !newContent.EndsWith(Environment.NewLine)) newContent += Environment.NewLine;
        
        try
        {
            File.WriteAllText(fullPath, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            summary.SuccessOps += localSuccessOps;
            summary.Log.AddRange(localLogs);
            summary.Log.Add($"[写盘成功] {fullPath}");
        }
        catch (Exception ex)
        {
            summary.Log.Add($"[写盘失败] {fullPath} : {ex.Message}");
            throw; // 抛出异常以便上层捕获并计入失败
        }
    }

    private static int OpBaseIndex(LineOp raw, int start0, int after0)
        => raw.Type.ToLowerInvariant() switch
        {
            "insert" => after0,
            _ => start0,
        };

    /// <summary>
    /// 将 AI 返回的代码内容按真实换行拆分成行，同时保留源码里的转义字符文本。
    /// </summary>
    private static string[] SplitContentLines(string? content)
    {
        return (content ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
    }

    private ApplyResult ApplyOneOp(List<string> lines, LineOp op, int start0, int end0, int after0)
    {
        var type = (op.Type ?? "").ToLowerInvariant();
        
        // 如果有 OldContent，进行模糊搜索定位真实的 start0 和 end0
        if (type == "replace" && op.OldContent != null && lines.Count > 0)
        {
            var oldBlock = SplitContentLines(op.OldContent);
            if (oldBlock.Length > 0)
            {
                int bestMatchStart = -1;
                // 搜索范围：以提供的 start0 为中心，上下浮动 50 行
                int searchStart = Math.Max(0, start0 - 50);
                int searchEnd = Math.Min(lines.Count - oldBlock.Length, start0 + 50);
                
                for (int i = searchStart; i <= searchEnd; i++)
                {
                    bool match = true;
                    for (int j = 0; j < oldBlock.Length; j++)
                    {
                        if (lines[i + j].Trim() != oldBlock[j].Trim())
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        // 如果有多个匹配，优先选择最接近 start0 的
                        if (bestMatchStart == -1 || Math.Abs(i - start0) < Math.Abs(bestMatchStart - start0))
                        {
                            bestMatchStart = i;
                        }
                    }
                }

                if (bestMatchStart == -1 && oldBlock.Length <= lines.Count)
                {
                    for (int i = 0; i <= lines.Count - oldBlock.Length; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < oldBlock.Length; j++)
                        {
                            if (lines[i + j].Trim() != oldBlock[j].Trim())
                            {
                                match = false;
                                break;
                            }
                        }
                        if (match)
                        {
                            bestMatchStart = i;
                            break;
                        }
                    }
                }
                
                if (bestMatchStart != -1)
                {
                    start0 = bestMatchStart;
                    end0 = bestMatchStart + oldBlock.Length - 1;
                }
                else
                {
                    var newBlock = SplitContentLines(op.Content);
                    var newBlockMatchStart = FindBestMatchingBlock(lines, newBlock, start0);
                    if (newBlockMatchStart != -1)
                    {
                        return new ApplyResult
                        {
                            Success = true,
                            Message = $"补丁目标内容已存在，第 {newBlockMatchStart + 1} 行附近跳过重复应用"
                        };
                    }

                    var rangeMatches = BlockMatchesAt(lines, start0, oldBlock);
                    if (!rangeMatches)
                    {
                        var preview = string.Join(" | ", oldBlock.Take(3).Select(x => x.Trim()));
                        return new ApplyResult
                        {
                            Success = false,
                            Message = $"未找到与补丁上下文匹配的原文块，已阻止替换。片段：{preview}"
                        };
                    }
                }
            }
        }

        switch (type)
        {
            case "replace":
                // 彻底放宽：如果是新文件或者目标文件内容为空，且AI发来了 replace，全部当做无条件插入
                if (lines.Count == 0)
                {
                    var newLinesEmpty = SplitContentLines(op.Content);
                    lines.AddRange(newLinesEmpty);
                    return new ApplyResult { Success = true, Message = $"文件为空，直接作为新内容插入 {newLinesEmpty.Length} 行" };
                }

                // 彻底放宽：如果 start0 已经超过了文件末尾，说明 AI 以为文件很长，实际上文件没那么长。
                // 这时候我们把原文件内容保留，直接把新内容追加到最后，避免因为行号对不上导致整个修改失败。
                if (start0 >= lines.Count)
                {
                    var appendLines = SplitContentLines(op.Content);
                    lines.AddRange(appendLines);
                    return new ApplyResult { Success = true, Message = $"行号超限({start0 + 1} > {lines.Count})，已作为追加处理" };
                }

                // 常规越界保护
                if (start0 < 0 || start0 > end0)
                {
                    return new ApplyResult { Success = false, Message = $"无效的 replace 区间 {start0 + 1}-{end0 + 1}（共 {lines.Count} 行）" };
                }
                
                // 防止 end0 越界：如果要求替换到 100 行，但文件只有 5 行，那就把 1 到 5 行替换掉
                int safeEnd0 = Math.Min(end0, lines.Count - 1);
                
                var newLines = SplitContentLines(op.Content);
                lines.RemoveRange(start0, safeEnd0 - start0 + 1);
                lines.InsertRange(start0, newLines);
                return new ApplyResult { Success = true, Message = $"第 {start0 + 1}-{safeEnd0 + 1} 行替换为 {newLines.Length} 行" };

            case "delete":
                if (start0 < 0 || end0 >= lines.Count || start0 > end0)
                    return new ApplyResult { Success = false, Message = $"行号越界 delete {start0 + 1}-{end0 + 1}" };
                lines.RemoveRange(start0, end0 - start0 + 1);
                return new ApplyResult { Success = true, Message = $"删除第 {start0 + 1}-{end0 + 1} 行" };

            case "insert":
                if (after0 < 0 || after0 > lines.Count)
                    return new ApplyResult { Success = false, Message = $"行号越界 insert after {after0}" };
                var ins = SplitContentLines(op.Content);
                lines.InsertRange(after0, ins);
                return new ApplyResult { Success = true, Message = $"在第 {after0} 行后插入 {ins.Length} 行" };

            default:
                return new ApplyResult { Success = false, Message = $"未知操作类型：{op.Type}" };
        }
    }

    /// <summary>
    /// 判断目标文件在给定起点处是否仍然包含补丁要求替换的旧内容。
    /// </summary>
    private static bool BlockMatchesAt(List<string> lines, int start0, string[] oldBlock)
    {
        if (start0 < 0 || oldBlock.Length == 0) return false;
        if (start0 + oldBlock.Length > lines.Count) return false;

        for (int i = 0; i < oldBlock.Length; i++)
        {
            if (!string.Equals(lines[start0 + i].Trim(), oldBlock[i].Trim(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 在当前文件中查找与目标块内容一致的位置；若已存在，则说明该补丁很可能已经应用过。
    /// </summary>
    private static int FindBestMatchingBlock(List<string> lines, string[] block, int preferredStart0)
    {
        if (block.Length == 0 || lines.Count < block.Length) return -1;

        var bestMatchStart = -1;
        for (int i = 0; i <= lines.Count - block.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < block.Length; j++)
            {
                if (!string.Equals(lines[i + j].Trim(), block[j].Trim(), StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (!match) continue;

            if (bestMatchStart == -1 || Math.Abs(i - preferredStart0) < Math.Abs(bestMatchStart - preferredStart0))
            {
                bestMatchStart = i;
            }
        }

        return bestMatchStart;
    }
}
