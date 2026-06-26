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
    /// <summary>
    /// 生成人工智能提示词，并弹窗展示，方便用户复制给外部 AI。
    /// </summary>
    private void BtnGen_Click(object? sender, EventArgs e)
    {
        if (_projectRoot == null) { Warn("请先打开一个目录"); return; }

        SaveCurrentPlanText(forceHistory: true);
        ShowPromptWindow();
        _lblStatus.Text = "● 提示词已生成";
        _lblStatus.ForeColor = Success;
        Log("下一步：复制提示词给 AI，把返回的 Patch/命令内容粘贴到“AI 返回内容”页，再点“应用”");
    }

    /// <summary>
    /// 展示提示词窗口，并提供一键复制能力。
    /// </summary>
    private void ShowPromptWindow()
    {
        var currentPrompt = GeneratePromptText(out var files) ?? "";
        
        using var dlg = new Form
        {
            Text = "AI 提示词",
            Width = 1000,
            Height = 700,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = BgDark,
            ForeColor = FgText,
        };
        var tb = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Dock = DockStyle.Fill,
            Font = new Font("Cascadia Mono", 10),
            Text = currentPrompt,
            ReadOnly = true,
            BackColor = BgDark,
            ForeColor = FgText,
            BorderStyle = BorderStyle.None,
        };
        var buttonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            ColumnCount = 3,
            BackColor = BgPanel,
            Padding = new Padding(8, 6, 8, 6),
        };
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        
        var chkIncludeAll = new CheckBox
        {
            Text = "包含全部项目文件",
            Checked = _chkIncludeAll.Checked,
            ForeColor = FgText,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 0, 16, 0),
            Cursor = Cursors.Hand
        };
        chkIncludeAll.CheckedChanged += (_, _) => 
        {
            _chkIncludeAll.Checked = chkIncludeAll.Checked;
            currentPrompt = GeneratePromptText(out files) ?? "";
            tb.Text = currentPrompt;
            Log($"✔ 提示词已重新生成（{currentPrompt.Length} 字符，带入了 {files.Count} 个文件）");
        };

        var btnAddProtocol = new Button
        {
            Text = PromptGenerator.ContainsFullJsonProtocol(currentPrompt) ? "✅ 已含 Patch 协议" : "🧩 加入 Patch 协议",
            Dock = DockStyle.Fill,
            BackColor = BgHeader,
            ForeColor = FgText,
            FlatStyle = FlatStyle.Flat,
            Enabled = !PromptGenerator.ContainsFullJsonProtocol(currentPrompt),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(0, 0, 6, 0),
        };
        var btnCopy = new Button
        {
            Text = "📋 复制到剪贴板",
            Dock = DockStyle.Fill,
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Margin = new Padding(6, 0, 0, 0),
        };
        btnAddProtocol.FlatAppearance.BorderSize = 0;
        btnCopy.FlatAppearance.BorderSize = 0;
        btnAddProtocol.Click += (_, _) =>
        {
            currentPrompt = PromptGenerator.EnsureFullJsonProtocol(tb.Text);
            tb.Text = currentPrompt;
            _lastGeneratedPrompt = currentPrompt;
            btnAddProtocol.Text = "✅ 已含 Patch 协议";
            btnAddProtocol.Enabled = false;
            Log("✔ 已将完整 Patch 协议说明加入当前提示词窗口，可直接复制给 AI。");
        };
        btnCopy.Click += (_, _) =>
        {
            try
            {
                currentPrompt = tb.Text ?? "";
                Clipboard.SetText(currentPrompt);
                _lastGeneratedPrompt = currentPrompt;
                btnCopy.Text = "✅ 已复制！";
                Log("✔ 已复制提示词，请粘贴给 AI；拿到 Patch/命令返回后粘回当前页并点“应用”");
                ShowTransientStatus("● 已复制提示词，可直接发给 AI", Success);
            }
            catch (Exception ex)
            {
                Log($"✖ 复制提示词失败：{ex.Message}");
                ShowTransientStatus("● 复制提示词失败，请看日志", Error);
            }
        };
        buttonPanel.Controls.Add(chkIncludeAll, 0, 0);
        buttonPanel.Controls.Add(btnAddProtocol, 1, 0);
        buttonPanel.Controls.Add(btnCopy, 2, 0);
        dlg.Controls.Add(tb);
        dlg.Controls.Add(buttonPanel);
        dlg.ShowDialog(this);
    }

    /// <summary>
    /// 复制最近一次生成的提示词；如果还没生成，则即时生成后再复制。
    /// </summary>
    private async void CopyPromptToClipboard()
    {
        if (_projectRoot == null)
        {
            Log("⚠ 请先打开目录，再复制提示词给 AI。");
            ShowTransientStatus("● 请先打开目录", Warning);
            return;
        }
        var prompt = _lastGeneratedPrompt;
        var files = GetCheckedFiles();
        SaveCurrentPlanText(forceHistory: true);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = GeneratePromptText(out files);
            if (prompt == null) return;
        }

        try
        {
            Clipboard.SetText(prompt);
            Log($"✔ 已复制提示词到剪贴板（{prompt.Length} 字符，带入了 {files.Count} 个文件）");
            var browserResult = await TrySendToAiBrowserAsync(prompt, autoSend: true, source: "prompt");
            if (browserResult.Ok)
            {
                UpdateAiBrowserSendStatus(browserResult, prompt);
            }
            else
            {
                UpdateAiBrowserSendStatus(browserResult, prompt);
                Log("· AI 浏览器未连接或页面未就绪，仍可使用剪贴板手工粘贴。");
            }
            Log("下一步：把提示词发给 AI，然后将返回的 Patch/命令内容粘贴");
            _lblStatus.Text = "● 等待粘贴 AI 返回内容";
            _lblStatus.ForeColor = Accent;
            ShowTransientStatus("● 已复制提示词，可直接发给 AI", Success);
        }
        catch (Exception ex)
        {
            Log($"✖ 复制提示词失败：{ex.Message}");
            ShowTransientStatus("● 复制提示词失败，请看日志", Error);
        }
    }

    /// <summary>
    /// 在粘贴返回内容页快速复制当前代码上下文和计划，方便重新发给 AI。
    /// </summary>
    private void BtnQuoteContext_Click(object? sender, EventArgs e)
    {
        CopyPromptToClipboard();
        Log("✔ 已通过“引用”按钮复制当前代码和计划，可直接发给 AI。");
    }

    /// <summary>
    /// 生成提示词并缓存到窗体状态，供展示和复制复用。
    /// </summary>
    private string? GeneratePromptText(out List<string> files, string? taskOverride = null)
    {
        files = new List<string>();
        if (_projectRoot == null) return null;

        var gen = new PromptGenerator();
        files = GetCheckedFiles();
        var includeJsonProtocol = _settings.IncludePromptProtocolOnNextPrompt;
        var prompt = gen.Generate(_projectRoot, files, taskOverride ?? _txtPlan.Text, _chkIncludeAll.Checked, includeJsonProtocol);
        if (includeJsonProtocol)
        {
            _settings.IncludePromptProtocolOnNextPrompt = false;
            _settings.Save();
        }
        _lastGeneratedPrompt = prompt;
        return prompt;
    }
}
