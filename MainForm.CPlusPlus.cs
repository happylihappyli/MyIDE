using System.Text.RegularExpressions;
using MyIDE.Forms;
using System.IO;
using System.Text.Encodings.Web;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System;
using System.Net.Http;
using MyIDE.Models;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using MyIDE.Services;

namespace MyIDE;

public partial class MainForm
{
    private string DetectCompiler()
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "clang++", Arguments = "--version", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            proc?.WaitForExit();
            return "clang++";
        }
        catch { }

        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "g++", Arguments = "--version", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            proc?.WaitForExit();
            return "g++";
        }
        catch { }

        return "g++"; // 默认回退到 g++，让它自然抛出找不到文件的异常
    }

    /// <summary>
    /// 切换到底部命令输出区，并按需写入新的阶段标题。
    /// </summary>
    private bool PrepareRunOutputPanel(string phaseTitle, bool clearOutput)
    {
        if (string.IsNullOrEmpty(_projectRoot))
        {
            MessageBox.Show("请先打开一个项目目录！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (clearOutput)
        {
            _txtRunOutput.Clear();
        }

        ActivateRunOutputTab();
        AppendRunOutput($"[{DateTime.Now:HH:mm:ss}] {phaseTitle}\n");
        return true;
    }

    /// <summary>
    /// 获取当前项目默认输出程序和编译参数。
    /// 仅在根目录查找 .cpp 文件。如果使用 Scons 编译或有特定的构建系统配置，
    /// 这里主要作为备用逻辑或快速验证脚本。
    /// </summary>
    private bool TryGetCppBuildContext(out string exePath, out string filesArgs)
    {
        exePath = Path.Combine(_projectRoot ?? "", "app_run.exe");
        filesArgs = "";

        if (string.IsNullOrEmpty(_projectRoot)) return false;

        var cppFiles = Directory.GetFiles(_projectRoot, "*.cpp", SearchOption.TopDirectoryOnly);
        if (cppFiles.Length == 0)
        {
            // 对于 scons 构建或者不依赖根目录 cpp 文件的项目，不要报错拦截，
            // 而是返回一个空的参数，让外部调用者可以尝试调用更高级的构建命令
            return true;
        }

        filesArgs = string.Join(" ", cppFiles.Select(f => $"\"{Path.GetFileName(f)}\""));
        return true;
    }

    /// <summary>
    /// 按经典工作流先编译再运行，适合作为一键验证入口。
    /// </summary>
    private async void RunCppCode()
    {
        if (!PrepareRunOutputPanel("准备编译并运行 C++ 代码...", clearOutput: true)) return;
        if (!TryGetCppBuildContext(out var exePath, out var filesArgs)) return;

        try
        {
            var buildOk = await BuildCppProjectCoreAsync(exePath, filesArgs);
            if (!buildOk) return;

            AppendRunOutput(new string('-', 40) + "\n");
            await RunCppProjectCoreAsync(exePath);
        }
        catch (Exception ex)
        {
            AppendRunOutput($"❌ 发生异常：{ex.Message}\n");
        }
    }

    /// <summary>
    /// 将终端输出内容作为错误上下文发送给 AI，供其他窗体或模块调用
    /// </summary>
    public async Task AutoExtractAndSendErrorAsync(string errorReason, string outputText)
    {
        if (string.IsNullOrWhiteSpace(outputText)) return;

        AppendRunOutput($"\n[{DateTime.Now:HH:mm:ss}] ⚠️ 检测到{errorReason}，正在自动将错误信息发送给 AI...\n", Color.Orange);

        var isCompileRelated = IsCompileRelatedErrorReason(errorReason);
        var taskOverride = isCompileRelated ? BuildCompileErrorTaskOverride() : null;

        string prompt = GeneratePromptText(out var files, taskOverride) ?? "";

        string content = prompt;
        if (!string.IsNullOrWhiteSpace(content))
        {
            content += "\n\n";
        }

        content += "======================================\n";
        content += $"以下是程序最新的{errorReason}结果：\n\n```text\n" + outputText + "\n```\n\n";
        content += BuildErrorFollowUpInstruction(isCompileRelated);

        try
        {
            Clipboard.SetText(content);
            var browserResult = await TrySendToAiBrowserAsync(content, autoSend: true, source: "auto_error_report");
            if (browserResult.Ok)
            {
                UpdateAiBrowserSendStatus(browserResult, content);
                AppendRunOutput($"[{DateTime.Now:HH:mm:ss}] ✅ 已成功将错误信息自动发送给 AI，请前往 MyChrome 查看。\n", Color.Green);
            }
            else
            {
                UpdateAiBrowserSendStatus(browserResult, content);
                AppendRunOutput($"[{DateTime.Now:HH:mm:ss}] ⚠️ 自动发送给 AI 失败，已保留在剪贴板，请手动粘贴。\n", Color.Orange);
            }
        }
        catch (Exception ex)
        {
            Warn($"自动发送错误给 AI 失败：{ex.Message}");
            AppendRunOutput($"[{DateTime.Now:HH:mm:ss}] ❌ 自动发送失败：{ex.Message}\n", Color.Red);
        }
    }

    /// <summary>
    /// 将终端输出内容作为错误上下文发送给 AI（重载，默认使用当前运行面板的输出）
    /// </summary>
    private async Task AutoExtractAndSendErrorAsync(string errorReason)
    {
        await AutoExtractAndSendErrorAsync(errorReason, _txtRunOutput.Text);
    }

    /// <summary>
    /// 判断当前错误是否属于编译相关场景，用于决定是否忽略计划区内容。
    /// </summary>
    private static bool IsCompileRelatedErrorReason(string errorReason)
    {
        return !string.IsNullOrWhiteSpace(errorReason) &&
               errorReason.Contains("编译", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 根据当前输出文本判断是否属于编译失败场景，供“复制给 AI”按钮决定是否忽略计划区内容。
    /// </summary>
    private static bool IsCompileFailureOutput(string outputText)
    {
        if (string.IsNullOrWhiteSpace(outputText)) return false;

        return outputText.Contains("编译失败", StringComparison.OrdinalIgnoreCase) ||
               outputText.Contains("编译超时", StringComparison.OrdinalIgnoreCase) ||
               outputText.Contains("编译阶段发生异常", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 生成编译错误场景下的任务说明，避免把计划区内容再次发给 AI。
    /// </summary>
    private static string BuildCompileErrorTaskOverride()
    {
        return "请仅根据下面提供的编译错误结果分析并修复问题，不要沿用或复制之前计划里的内容。请优先定位导致编译失败的直接原因，并给出最小必要修改。";
    }

    /// <summary>
    /// 根据场景生成发送给 AI 的结尾指令，保持自动发送和手动复制的行为一致。
    /// </summary>
    private static string BuildErrorFollowUpInstruction(bool isCompileRelated)
    {
        return isCompileRelated
            ? "请结合上述代码和编译结果，分析并修复导致当前编译失败的问题。请严格按 Patch 协议返回：代码修改放在【补丁】区段，命令放在【命令】区段的 JSON 数组里；如果没有命令也要返回 []。不要输出额外解释。"
            : "请结合上述代码和运行结果，分析并修复问题，或者继续完成下一步计划。请严格按 Patch 协议返回：代码修改放在【补丁】区段，命令放在【命令】区段的 JSON 数组里；如果没有命令也要返回 []。不要输出额外解释。";
    }

    private async void BtnCopyOutput_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtRunOutput.Text)) return;

        var isCompileRelated = IsCompileFailureOutput(_txtRunOutput.Text);
        var taskOverride = isCompileRelated ? BuildCompileErrorTaskOverride() : null;
        string prompt = GeneratePromptText(out var files, taskOverride) ?? "";

        string content = prompt;
        if (!string.IsNullOrWhiteSpace(content))
        {
            content += "\n\n";
        }
        
        content += "======================================\n";
        content += isCompileRelated
            ? "以下是程序的最新编译错误结果：\n\n```text\n" + _txtRunOutput.Text + "\n```\n\n"
            : "以下是程序的最新编译和运行结果：\n\n```text\n" + _txtRunOutput.Text + "\n```\n\n";
        content += BuildErrorFollowUpInstruction(isCompileRelated);

        try
        {
            Clipboard.SetText(content);
            Log("✔ 已将提示词和运行输出组合复制到剪贴板，可直接粘贴给 AI。");
            var browserResult = await TrySendToAiBrowserAsync(content, autoSend: true, source: "run_output");
            if (browserResult.Ok)
            {
                UpdateAiBrowserSendStatus(browserResult, content);
            }
            else
            {
                UpdateAiBrowserSendStatus(browserResult, content);
                Log("· 已保留剪贴板兜底。请确认 MyWebView2Browser 已启动并打开 AI 页面。");
            }
            ShowTransientStatus("● 已复制给 AI，可直接粘贴", Success);
        }
        catch (Exception ex)
        {
            Log($"✖ 复制给 AI 失败：{ex.Message}");
            ShowTransientStatus("● 复制给 AI 失败，请看日志", Error);
        }
    }
}
