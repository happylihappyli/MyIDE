using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MyIDE.Services;

/// <summary>
/// 提示词生成器：根据用户的"计划" + 项目结构 + 当前文件，组装出可粘贴给 AI 的完整提示词
/// </summary>
public class PromptGenerator
{
    /// <summary>
    /// 生成完整提示词
    /// </summary>
    /// <param name="projectRoot">项目根目录（绝对路径）</param>
    /// <param name="selectedFiles">用户选中的文件列表（相对路径）</param>
    /// <param name="userPlan">用户写的"我要做什么"</param>
    /// <param name="includeAllFiles">是否把所有文件内容都塞进去（文件很多时建议 false）</param>
    public string Generate(string projectRoot, List<string> selectedFiles, string userPlan, bool includeAllFiles)
    {
        var sb = new StringBuilder();

        // 1. 角色与输出规范
        sb.AppendLine("你是一个代码修改助手。请根据【任务说明】修改【项目】中的文件，并在同一份 JSON 中同时给出需要执行的命令。");
        sb.AppendLine("我会把你返回的 JSON 直接粘贴回 IDE 并点击“应用”，所以你的输出必须是可直接解析的纯 JSON。");
        sb.AppendLine();
        sb.AppendLine("【输出 JSON 规范】");
        sb.AppendLine(@"{
  ""task"": ""一句话说明这次做了什么"",
  ""changes"": [
    {
      ""file"": ""相对项目根目录的路径，如 src/main.cpp"",
      ""ops"": [
        { ""type"": ""replace"", ""start"": 10, ""end"": 15, ""content"": ""替换后的新内容（多行用 \n）"" },
        { ""type"": ""insert"",  ""after"": 20,                ""content"": ""在第 20 行后插入的内容"" },
        { ""type"": ""delete"",  ""start"": 30, ""end"": 32 }
      ]
    }
  ],
  ""commands"": [
    {
      ""name"": ""命令名称，如 编译项目"",
      ""reason"": ""为什么要执行这条命令"",
      ""command"": ""要执行的命令文本，Windows 下优先返回 PowerShell 兼容命令"",
      ""shell"": ""powershell"",
      ""workingDirectory"": ""相对项目根目录的工作目录，项目根目录可写 . 或 空字符串"",
      ""optional"": false
    }
  ]
}");
        sb.AppendLine();
        sb.AppendLine("【返回示例】");
        sb.AppendLine(@"{
  ""task"": ""修复 Windows 头文件包含顺序并给出重新编译命令"",
  ""changes"": [
    {
      ""file"": ""main.cpp"",
      ""ops"": [
        { ""type"": ""replace"", ""start"": 1, ""end"": 3, ""content"": ""#include <windows.h>\n#include <commctrl.h>\nint main() { return 0; }"" }
      ]
    }
  ],
  ""commands"": [
    {
      ""name"": ""重新编译"",
      ""reason"": ""验证修改后的代码是否通过编译"",
      ""command"": ""clang++ main.cpp -o app_run.exe -lcomctl32"",
      ""shell"": ""powershell"",
      ""workingDirectory"": ""."",
      ""optional"": false
    },
    {
      ""name"": ""运行程序"",
      ""reason"": ""编译成功后运行程序进行验证"",
      ""command"": "".\app_run.exe"",
      ""shell"": ""powershell"",
      ""workingDirectory"": ""."",
      ""optional"": true
    }
  ]
}");
        sb.AppendLine();
        sb.AppendLine("【硬性约束】");
        sb.AppendLine("1. 只输出上述 JSON，不要任何解释、Markdown 代码块、或其他文字");
        sb.AppendLine("2. 行号从 1 开始，包含起止行");
        sb.AppendLine("3. content 里的换行用 \\n，双引号用 \\\"");
        sb.AppendLine("4. 一次只做必要的修改，不要顺手重构");
        sb.AppendLine("5. 如果需要创建新文件，file 写新文件相对路径，ops 使用 insert，after 固定写 0，content 写完整文件内容");
        sb.AppendLine("6. 如果目标文件当前不存在，不要报错，按创建新文件处理");
        sb.AppendLine("7. 如果不需要修改文件，changes 返回 []；如果不需要执行命令，commands 返回 []");
        sb.AppendLine("8. commands 里的命令必须是当前任务真正需要执行的命令，不要返回解释性命令示例");
        sb.AppendLine("9. Windows 环境优先返回 PowerShell 可直接执行的命令");
        sb.AppendLine("10. workingDirectory 必须写相对项目根目录的路径，不要写绝对路径");
        sb.AppendLine();
        sb.AppendLine("【命令返回协议】");
        sb.AppendLine("1. 所有命令必须放进 JSON 的 commands 数组里，不要放在 JSON 外面");
        sb.AppendLine("2. 不要返回 shell 代码块、powershell 代码块、bash 代码块；命令只允许出现在 commands[*].command");
        sb.AppendLine("3. 如果某条命令只是参考，不应执行，就不要放进 commands");
        sb.AppendLine("4. 如果修改完成后需要重新编译、运行、测试、安装依赖，请把对应命令写进 commands");
        sb.AppendLine("5. commands 为空时必须显式返回 []");
        sb.AppendLine("6. 如果当前任务包含编译报错、运行报错、依赖缺失、测试失败，优先返回用于验证修复结果的命令");
        sb.AppendLine("7. 如果只需要改代码不需要执行命令，必须返回 \"commands\": []，不要省略这个字段");
        sb.AppendLine();

        // 2. 任务说明
        sb.AppendLine("【任务说明】");
        sb.AppendLine(string.IsNullOrWhiteSpace(userPlan) ? "（用户未填写）" : userPlan.Trim());
        sb.AppendLine();

        if (selectedFiles != null && selectedFiles.Count > 0)
        {
            sb.AppendLine("【本次优先处理的文件】");
            foreach (var relPath in selectedFiles)
                sb.AppendLine(relPath);
            sb.AppendLine();
        }

        // 3. 项目根目录
        sb.AppendLine("【项目根目录】");
        sb.AppendLine(projectRoot);
        sb.AppendLine();

        // 4. 当前关注的文件内容（带行号）
        if (selectedFiles != null && selectedFiles.Count > 0)
        {
            sb.AppendLine("【当前关注的文件】");
            foreach (var relPath in selectedFiles)
            {
                var fullPath = Path.Combine(projectRoot, relPath);
                if (File.Exists(fullPath))
                {
                    sb.AppendLine($"--- {relPath} ---");
                    sb.AppendLine("```");
                    AppendFileWithLineNumbers(sb, fullPath);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        // 5. 目录结构
        sb.AppendLine("【目录结构】（最多展示 200 个文件，已过滤常见构建/依赖目录）");
        AppendDirectoryTree(sb, projectRoot);
        sb.AppendLine();

        // 6. 其他相关文件
        if (includeAllFiles)
        {
            sb.AppendLine("【项目全部文件内容】");
            var files = Directory
                .EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories)
                .Where(ShouldIncludeFile)
                .Take(200)
                .ToList();
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(projectRoot, f).Replace('\\', '/');
                sb.AppendLine($"--- {rel} ---");
                AppendFileWithLineNumbers(sb, f);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("（未勾选\"包含全部文件\"，如需其它文件请告诉我）");
        }

        return sb.ToString();
    }

    /// <summary>把文件按"行号: 内容"格式写入，跳过超大文件</summary>
    private static void AppendFileWithLineNumbers(StringBuilder sb, string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 512 * 1024)
            {
                sb.AppendLine($"（文件过大 {info.Length / 1024} KB，已跳过）");
                return;
            }
            var lines = File.ReadAllLines(path);
            var width = lines.Length.ToString().Length;
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append((i + 1).ToString().PadLeft(width));
                sb.Append(": ");
                sb.AppendLine(lines[i]);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"（读取失败：{ex.Message}）");
        }
    }

    /// <summary>生成目录树文本</summary>
    private static void AppendDirectoryTree(StringBuilder sb, string root)
    {
        try
        {
            int count = 0;
            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (!ShouldIncludeFile(path)) continue;
                count++;
                if (count > 200)
                {
                    sb.AppendLine("（文件数过多，后面的省略）");
                    break;
                }
                var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                sb.AppendLine(rel);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"（扫描失败：{ex.Message}）");
        }
    }

    /// <summary>过滤掉不需要的目录/文件（构建产物、依赖、.git 等）</summary>
    private static bool ShouldIncludeFile(string path)
    {
        var skipDirs = new[]
        {
            "\\.git\\", "\\.vs\\", "\\bin\\", "\\obj\\", "\\node_modules\\",
            "\\target\\", "\\build\\", "\\dist\\", "\\.idea\\", "\\.vscode\\",
            "\\__pycache__\\", "\\venv\\", "\\.venv\\"
        };
        var norm = path.Replace('/', '\\');
        foreach (var s in skipDirs)
            if (norm.Contains(s, StringComparison.OrdinalIgnoreCase)) return false;

        var skipExt = new[] { ".exe", ".dll", ".pdb", ".cache", ".suo", ".user", ".zip", ".rar", ".7z", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf" };
        var ext = Path.GetExtension(path).ToLowerInvariant();
        foreach (var s in skipExt)
            if (ext == s) return false;

        return true;
    }
}
