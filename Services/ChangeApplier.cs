using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    /// <summary>
    /// 解析 AI 返回的 JSON 文本（容忍 ```json 包裹、容忍前后杂讯）
    /// </summary>
    public ChangePlan ParseJson(string rawText)
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
        return JsonSerializer.Deserialize<ChangePlan>(text, options)
            ?? throw new InvalidDataException("JSON 解析结果为空");
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
        List<string> lines;
        bool isNewFile = !File.Exists(fullPath);
        
        if (isNewFile)
        {
            // 文件不存在，创建新文件
            lines = new List<string>();
            summary.Log.Add($"[创建] {fc.File}");
            
            // 确保父目录存在
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                summary.Log.Add($"[创建目录] {dir}");
            }
        }
        else
        {
            lines = File.ReadAllLines(fullPath).ToList();
        }

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

        summary.Log.Add($"[调试] 准备对 {fc.File} 应用 {ops.Count} 个操作，当前文件行数: {lines.Count}");

        // 只有文件已存在时才创建备份
        if (createBackup && !isNewFile)
        {
            var backupPath = fullPath + ".bak";
            File.Copy(fullPath, backupPath, overwrite: true);
        }

        foreach (var op in ops)
        {
            var r = ApplyOneOp(lines, op.Raw, op.Start0, op.End0, op.After0);
            if (r.Success) summary.SuccessOps++;
            summary.Log.Add($"[{fc.File}] {op.Raw.Type} -> {r.Message}");
        }

        // 写回文件（统一保证以换行符结尾）
        var newContent = string.Join(Environment.NewLine, lines);
        if (newContent.Length > 0 && !newContent.EndsWith(Environment.NewLine)) newContent += Environment.NewLine;
        File.WriteAllText(fullPath, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static int OpBaseIndex(LineOp raw, int start0, int after0)
        => raw.Type.ToLowerInvariant() switch
        {
            "insert" => after0,
            _ => start0,
        };

    private ApplyResult ApplyOneOp(List<string> lines, LineOp op, int start0, int end0, int after0)
    {
        var type = (op.Type ?? "").ToLowerInvariant();
        switch (type)
        {
            case "replace":
                // 彻底放宽：如果是新文件或者目标文件内容为空，且AI发来了 replace，全部当做无条件插入
                if (lines.Count == 0)
                {
                    var newLinesEmpty = (op.Content ?? "").Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\t", "\t").Split('\n');
                    lines.AddRange(newLinesEmpty);
                    return new ApplyResult { Success = true, Message = $"文件为空，直接作为新内容插入 {newLinesEmpty.Length} 行" };
                }

                // 彻底放宽：如果 start0 已经超过了文件末尾，说明 AI 以为文件很长，实际上文件没那么长。
                // 这时候我们把原文件内容保留，直接把新内容追加到最后，避免因为行号对不上导致整个修改失败。
                if (start0 >= lines.Count)
                {
                    var appendLines = (op.Content ?? "").Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\t", "\t").Split('\n');
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
                
                var newLines = (op.Content ?? "").Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\t", "\t").Split('\n');
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
                var ins = (op.Content ?? "").Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\t", "\t").Split('\n');
                lines.InsertRange(after0, ins);
                return new ApplyResult { Success = true, Message = $"在第 {after0} 行后插入 {ins.Length} 行" };

            default:
                return new ApplyResult { Success = false, Message = $"未知操作类型：{op.Type}" };
        }
    }
}
