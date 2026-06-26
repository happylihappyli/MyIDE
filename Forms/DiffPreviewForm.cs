using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyIDE.Services;

namespace MyIDE.Forms;

/// <summary>
/// 差异预览窗体：左侧原文件，右侧新文件，高亮显示增/删/改
/// </summary>
public class DiffPreviewForm : Form
{
    // 颜色方案（暗色主题）
    private static readonly Color BgDark = Color.FromArgb(30, 30, 30);
    private static readonly Color BgPanel = Color.FromArgb(45, 45, 48);
    private static readonly Color FgText = Color.FromArgb(212, 212, 212);
    private static readonly Color SameLineBg = Color.FromArgb(30, 30, 30);
    private static readonly Color AddedBg = Color.FromArgb(40, 80, 40);     // 绿
    private static readonly Color RemovedBg = Color.FromArgb(110, 40, 40);  // 红
    private static readonly Color LineNoFg = Color.FromArgb(110, 110, 110);

    private readonly List<ChangeApplier.SimulatedFile> _files;
    private readonly string _task;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _lblSummary = new() { Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgPanel, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
    private readonly Button _btnApply = new() { Text = "✅ 应用修改", Width = 160, Height = 40, BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    private readonly Button _btnCancel = new() { Text = "取消", Width = 80, Height = 32, BackColor = Color.FromArgb(60, 60, 60), ForeColor = FgText, FlatStyle = FlatStyle.Flat };
    private readonly Button _btnExportPatch = new() { Text = "📦 导出 Patch", Width = 110, Height = 32, BackColor = Color.FromArgb(60, 60, 60), ForeColor = FgText, FlatStyle = FlatStyle.Flat };

    /// <summary>用户点击"应用"后置 true，关闭后由调用方检查</summary>
    public bool Accepted { get; private set; }

    public DiffPreviewForm(string task, List<ChangeApplier.SimulatedFile> files)
    {
        _files = files;
        _task = task;
        Text = $"差异预览 - {task}（{files.Count} 个文件）";
        Width = 1300;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = BgDark;
        ForeColor = FgText;
        Font = new Font("Segoe UI", 9);

        BuildLayout();
        UpdateSummary();

        _btnApply.DialogResult = DialogResult.OK;
        _btnCancel.DialogResult = DialogResult.Cancel;
        
        // 当窗口关闭时，只要 DialogResult 是 OK，就视为接受
        FormClosing += (s, e) => {
            if (DialogResult == DialogResult.OK) Accepted = true;
        };
        
        _btnExportPatch.Click += BtnExportPatch_Click;
    }

    private void BtnExportPatch_Click(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog { Filter = "Patch 文件 (*.patch)|*.patch|所有文件 (*.*)|*.*", DefaultExt = "patch", FileName = "changes.patch" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in _files)
            {
                if (f.OriginalContent == f.NewContent) continue;
                var before = f.OriginalContent.Replace("\r\n", "\n").Split('\n');
                var after = f.NewContent.Replace("\r\n", "\n").Split('\n');
                var diff = DiffEngine.Diff(before, after);
                var (add, rem, _) = DiffEngine.Summarize(diff);
                if (add == 0 && rem == 0) continue;

                sb.AppendLine($"--- a/{f.RelativePath}");
                sb.AppendLine($"+++ b/{f.RelativePath}");
                sb.AppendLine($"@@ -1,{before.Length} +1,{after.Length} @@");
                foreach (var line in diff)
                {
                    if (line.Kind == DiffEngine.LineKind.Same) sb.AppendLine(" " + line.Text);
                    else if (line.Kind == DiffEngine.LineKind.Removed) sb.AppendLine("-" + line.Text);
                    else if (line.Kind == DiffEngine.LineKind.Added) sb.AppendLine("+" + line.Text);
                }
            }
            try
            {
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(false));
                MessageBox.Show(this, "Patch 导出成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void BuildLayout()
    {
        // 顶部按钮栏
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = BgPanel };
        _btnApply.Location = new Point(8, 5);
        _btnCancel.Location = new Point(176, 5);
        _btnExportPatch.Location = new Point(264, 5);
        topPanel.Controls.Add(_btnApply);
        topPanel.Controls.Add(_btnCancel);
        topPanel.Controls.Add(_btnExportPatch);

        // 摘要栏 - 忽略 [调试] 级别和创建文件的信息，只统计真正的失败
        // 降低禁用"应用"按钮的阈值，让用户能强制应用
        var totalErrors = _files.Sum(f => f.Issues.Count(i => i.Contains("失败") && !i.Contains("无效的 replace") && !i.Contains("行号越界")));
        // if (totalErrors > 0)
        //     _btnApply.Enabled = false;

        Controls.Add(_tabs);
        Controls.Add(_lblSummary);
        Controls.Add(topPanel);

        // 每个文件建一个 Tab
        foreach (var f in _files)
        {
            var page = new TabPage(Path.GetFileName(f.RelativePath)) { BackColor = BgDark };
            page.Controls.Add(BuildDiffView(f));
            _tabs.TabPages.Add(page);
        }
        if (_tabs.TabPages.Count == 0)
        {
            _tabs.TabPages.Add(new TabPage("（无修改）"));
        }
    }

    private void UpdateSummary()
    {
        int totalAdded = 0, totalRemoved = 0;
        foreach (var f in _files)
        {
            var d = DiffEngine.Diff(
                f.OriginalContent.Replace("\r\n", "\n").Split('\n'),
                f.NewContent.Replace("\r\n", "\n").Split('\n'));
            var (a, r, _) = DiffEngine.Summarize(d);
            totalAdded += a;
            totalRemoved += r;
        }
        var allIssues = _files.SelectMany(f => f.Issues).ToList();
        var realErrors = allIssues.Where(i => i.Contains("失败")).ToList();
        var warnings = allIssues.Where(i => i.Contains("将创建新文件")).ToList();
        var debugs = allIssues.Where(i => i.Contains("[调试]")).ToList();
        
        var sb = new System.Text.StringBuilder();
        sb.Append($"任务：{_task}    ");
        sb.Append($"文件 {_files.Count} 个    ");
        sb.Append($"+{totalAdded} 行    ");
        sb.Append($"-{totalRemoved} 行    ");
        
        if (realErrors.Count > 0)
        {
            sb.Append($"❌ {realErrors.Count} 个错误（应用按钮已禁用）");
            _lblSummary.BackColor = Color.FromArgb(120, 40, 40);
        }
        else if (warnings.Count > 0)
        {
            sb.Append($"⚠ {warnings.Count} 个提示（将创建新文件）");
            _lblSummary.BackColor = Color.FromArgb(120, 100, 40);
        }
        else if (debugs.Count > 0)
        {
            sb.Append($"ℹ {debugs.Count} 条调试信息");
            _lblSummary.BackColor = BgPanel;
        }
        else
        {
            _lblSummary.BackColor = BgPanel;
        }
        _lblSummary.Text = sb.ToString();
    }

    /// <summary>为单个文件创建并排 diff 视图</summary>
    private Control BuildDiffView(ChangeApplier.SimulatedFile f)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = BgDark,
        };
        // 初始设置为窗口宽度的一半
        split.SplitterDistance = Width / 2;
        // 在缩放时始终保持左右 50/50 的比例
        split.Resize += (s, e) => 
        {
            if (split.Width > 0)
            {
                split.SplitterDistance = split.Width / 2;
            }
        };

        split.Panel1.BackColor = BgDark;
        split.Panel2.BackColor = BgDark;

        var beforeBox = MakeRichBox();
        var afterBox = MakeRichBox();

        split.Panel1.Controls.Add(beforeBox);
        split.Panel2.Controls.Add(afterBox);

        var beforeLines = f.OriginalContent.Replace("\r\n", "\n").Split('\n');
        var afterLines = f.NewContent.Replace("\r\n", "\n").Split('\n');
        var diff = DiffEngine.Diff(beforeLines, afterLines);

        // 渲染前（左）
        foreach (var line in diff)
        {
            if (line.Kind == DiffEngine.LineKind.Added) continue;
            AppendDiffLine(beforeBox, line.OldLineNo, line.Text, line.Kind);
        }
        // 渲染后（右）
        foreach (var line in diff)
        {
            if (line.Kind == DiffEngine.LineKind.Removed) continue;
            AppendDiffLine(afterBox, line.NewLineNo, line.Text, line.Kind);
        }

        // 顶部问题提示
        if (f.Issues.Count > 0)
        {
            var lbl = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                BackColor = Color.FromArgb(120, 40, 40),
                ForeColor = Color.White,
                Text = "  " + string.Join("    ", f.Issues),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            split.Panel1.Controls.Add(lbl);
            split.Panel1.Controls.SetChildIndex(lbl, 0);
        }
        return split;
    }

    private static RichTextBox MakeRichBox()
    {
        return new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = SameLineBg,
            ForeColor = FgText,
            Font = new Font("Cascadia Mono", 10),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            BorderStyle = BorderStyle.None,
        };
    }

    private static void AppendDiffLine(RichTextBox box, int lineNo, string text, DiffEngine.LineKind kind)
    {
        // 行号
        var noStr = (lineNo == 0 ? " " : lineNo.ToString()).PadLeft(5);
        box.SelectionStart = box.TextLength;
        box.SelectionColor = LineNoFg;
        box.AppendText(noStr + "  ");

        // 内容
        box.SelectionStart = box.TextLength;
        switch (kind)
        {
            case DiffEngine.LineKind.Removed:
                box.SelectionBackColor = RemovedBg;
                box.SelectionColor = FgText;
                box.AppendText("- " + text + "\n");
                break;
            case DiffEngine.LineKind.Added:
                box.SelectionBackColor = AddedBg;
                box.SelectionColor = FgText;
                box.AppendText("+ " + text + "\n");
                break;
            default:
                box.SelectionBackColor = SameLineBg;
                box.SelectionColor = FgText;
                box.AppendText("  " + text + "\n");
                break;
        }
        // 重置背景为默认（避免后续行受影响）
        box.SelectionBackColor = SameLineBg;
        box.SelectionColor = FgText;
    }
}
