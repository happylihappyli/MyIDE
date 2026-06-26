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
    private const string FullPatchProtocolMarker = "【输出 Patch 协议】";
    private const string BriefPatchProtocolLine1 = "继续沿用当前 Session 已约定的 Patch 协议，本次不要重复解释协议内容。";
    private const string BriefPatchProtocolLine2 = "仍然优先返回 Patch 修改区段，并把命令单独放进【命令】区段中的 JSON 数组。";

    /// <summary>
    /// 生成完整提示词
    /// </summary>
    /// <param name="projectRoot">项目根目录（绝对路径）</param>
    /// <param name="selectedFiles">用户选中的文件列表（相对路径）</param>
    /// <param name="userPlan">用户写的"我要做什么"</param>
    /// <param name="includeAllFiles">是否把所有文件内容都塞进去（文件很多时建议 false）</param>
    /// <param name="includeJsonProtocol">是否在本次提示词中输出完整 Patch 协议说明</param>
    public string Generate(string projectRoot, List<string> selectedFiles, string userPlan, bool includeAllFiles, bool includeJsonProtocol = true)
    {
        var sb = new StringBuilder();

        // 1. 角色与输出规范
        sb.AppendLine("你是一个代码修改助手。请根据【任务说明】修改【项目】中的文件，并优先使用 Patch 协议返回代码改动。");
        if (includeJsonProtocol)
        {
            sb.Append(BuildFullJsonProtocolSection());
        }
        else
        {
            sb.Append(BuildBriefJsonProtocolSection());
        }

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

        // 3. 项目环境和约束
        sb.AppendLine("【项目环境与约束】");
        sb.AppendLine($"项目根目录: {projectRoot}");
        
        bool isCppProject = Directory.EnumerateFiles(projectRoot, "*.cpp", SearchOption.AllDirectories).Any() ||
                            Directory.EnumerateFiles(projectRoot, "*.h", SearchOption.AllDirectories).Any() ||
                            File.Exists(Path.Combine(projectRoot, "SConstruct"));
        bool usesScons = File.Exists(Path.Combine(projectRoot, "SConstruct"));

        if (isCppProject)
        {
            sb.AppendLine("当前操作系统：Windows");
            if (usesScons)
            {
                sb.AppendLine("构建系统：SCons (请使用 scons 命令进行编译)");
            }
            else
            {
                sb.AppendLine("编译器：g++");
            }
            sb.AppendLine("编码规范（极度重要）：源文件保存为 UTF-8 without BOM。如果修改 C++ 代码，必须全部使用 W 版本的 Win32 API，以确保不出现乱码！");
        }
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
            sb.AppendLine("【项目全部文件内容（已排除上面关注的文件）】");
            var files = Directory
                .EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories)
                .Where(ShouldIncludeFile)
                .Take(200)
                .ToList();
            
            int processedCount = 0;
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(projectRoot, f).Replace('\\', '/');
                // 避免重复罗列已在“当前关注的文件”中包含的内容，节省 Token
                if (selectedFiles != null && selectedFiles.Any(s => string.Equals(s, rel, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                sb.AppendLine($"--- {rel} ---");
                AppendFileWithLineNumbers(sb, f);
                sb.AppendLine();
                processedCount++;
            }

            if (processedCount == 0)
            {
                sb.AppendLine("（无其他补充文件）");
            }
        }
        else
        {
            sb.AppendLine("（未勾选\"包含全部文件\"，如需其它文件请告诉我）");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 返回完整的 Patch 协议说明，供新 Session 或手动补协议时复用。
    /// </summary>
    public static string BuildFullJsonProtocolSection()
    {
        var sb = new StringBuilder();
        sb.AppendLine("我会把你返回的内容直接粘贴回 IDE 并点击“预览/应用”，所以你的输出必须严格遵守下面的 Patch 协议。");
        sb.AppendLine();
        sb.AppendLine(FullPatchProtocolMarker);
        sb.AppendLine("你的完整输出必须按下面 3 个区段组织：");
        sb.AppendLine("1. 【任务】");
        sb.AppendLine("2. 【补丁】");
        sb.AppendLine("3. 【命令】");
        sb.AppendLine();
        sb.AppendLine("【返回示例】");
        sb.AppendLine(@"【任务】
修复 Windows 头文件包含顺序并给出重新编译命令

【补丁】
```patch
*** Begin Patch
*** Update File: main.cpp
@@
-#include <iostream>
+#include <windows.h>
+#include <commctrl.h>
+#include <iostream>
*** End Patch
```

【命令】
```json
[
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
```");
        sb.AppendLine();
        sb.AppendLine("【硬性约束】");
        sb.AppendLine("1. 必须包含【任务】、【补丁】、【命令】三个区段，不要输出其他解释性段落");
        sb.AppendLine("2. 【补丁】区段必须使用 patch 代码块，内部优先使用如下结构：*** Begin Patch / *** Add File / *** Update File / @@ / *** End Patch");
        sb.AppendLine("3. patch 中的新文件内容直接写原始文本，不要再做 JSON 式转义，不要把换行写成 \\n");
        sb.AppendLine("4. 如果不需要修改文件，【补丁】区段留空，或只写一段空 patch；不要伪造无意义修改");
        sb.AppendLine("5. 如果需要创建新文件，使用 *** Add File: 相对路径，然后每一行文件内容前面加 +");
        sb.AppendLine("6. 如果需要修改现有文件，使用 *** Update File: 相对路径，并尽量给出足够的上下文行");
        sb.AppendLine("7. 一次只做必要的修改，不要顺手大范围重构");
        sb.AppendLine("8. 所有路径都必须写相对项目根目录的路径，不要写绝对路径");
        sb.AppendLine();
        sb.AppendLine("【命令返回协议】");
        sb.AppendLine("1. 【命令】区段必须是一个 JSON 数组，可以放在 ```json 代码块里");
        sb.AppendLine("2. 每条命令字段固定为 name、reason、command、shell、workingDirectory、optional");
        sb.AppendLine("3. 如果不需要执行命令，返回 []");
        sb.AppendLine("4. 所有命令必须放进【命令】区段，不要写在区段外面");
        sb.AppendLine("5. Windows 环境优先返回 PowerShell 可直接执行的命令");
        sb.AppendLine("6. workingDirectory 必须写相对项目根目录的路径，不要写绝对路径");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// 返回简短的 Patch 协议提醒，供同一个 Session 后续轮次复用。
    /// </summary>
    public static string BuildBriefJsonProtocolSection()
    {
        var sb = new StringBuilder();
        sb.AppendLine(BriefPatchProtocolLine1);
        sb.AppendLine(BriefPatchProtocolLine2);
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// 判断提示词中是否已经包含完整 JSON 协议说明。
    /// </summary>
    public static bool ContainsFullJsonProtocol(string prompt)
    {
        return !string.IsNullOrWhiteSpace(prompt) &&
               prompt.Contains(FullPatchProtocolMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// 为现有提示词补上完整 JSON 协议说明；若已包含则原样返回。
    /// </summary>
    public static string EnsureFullJsonProtocol(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return BuildFullJsonProtocolSection();
        if (ContainsFullJsonProtocol(prompt)) return prompt;

        var fullSection = BuildFullJsonProtocolSection();
        var briefSection = BuildBriefJsonProtocolSection();
        if (prompt.Contains(BriefPatchProtocolLine1, StringComparison.Ordinal))
        {
            return prompt.Replace(briefSection, fullSection, StringComparison.Ordinal);
        }

        var firstBlockSeparator = prompt.IndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal);
        if (firstBlockSeparator >= 0)
        {
            return prompt.Insert(firstBlockSeparator + (Environment.NewLine + Environment.NewLine).Length, fullSection);
        }

        return prompt + Environment.NewLine + Environment.NewLine + fullSection;
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

        var skipExt = new[] { ".exe", ".dll", ".pdb", ".cache", ".suo", ".user", ".zip", ".rar", ".7z", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".pdf", ".dblite", ".db", ".sqlite", ".sqlite3" };
        var ext = Path.GetExtension(path).ToLowerInvariant();
        foreach (var s in skipExt)
            if (ext == s) return false;

        return true;
    }
}
