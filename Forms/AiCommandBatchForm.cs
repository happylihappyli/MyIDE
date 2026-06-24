using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyIDE.Models;

namespace MyIDE.Forms;

/// <summary>
/// AI 命令批量执行窗体，用于在应用修改后审核并执行 AI 返回的命令。
/// </summary>
public class AiCommandBatchForm : Form
{
    private readonly string _projectRoot;
    private readonly List<AiCommand> _commands;
    private readonly Action<IReadOnlyList<AiCommand>>? _saveSelectedCallback;
    private readonly CheckedListBox _list = new();
    private readonly TextBox _txtDetail = new();
    private readonly TextBox _txtOutput = new();
    private readonly Button _btnRunSelected = new();
    private readonly Button _btnSaveSelected = new();
    private readonly Button _btnClose = new();
    private bool _isRunning;

    /// <summary>
    /// 创建 AI 命令执行窗体。
    /// </summary>
    public AiCommandBatchForm(string projectRoot, IReadOnlyList<AiCommand> commands, Action<IReadOnlyList<AiCommand>>? saveSelectedCallback = null)
    {
        _projectRoot = projectRoot;
        _commands = commands.ToList();
        _saveSelectedCallback = saveSelectedCallback;

        Text = $"AI 命令执行（{_commands.Count} 条）";
        Width = 1100;
        Height = 760;
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadCommands();
    }

    /// <summary>
    /// 构建窗口布局。
    /// </summary>
    private void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = (int)(Width * 0.3)
        };
        Controls.Add(split);

        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        split.Panel1.Controls.Add(leftPanel);

        var lblList = new Label
        {
            Dock = DockStyle.Fill,
            Text = "  请选择要执行的命令",
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        leftPanel.Controls.Add(lblList, 0, 0);

        _list.Dock = DockStyle.Fill;
        _list.CheckOnClick = true;
        _list.HorizontalScrollbar = true;
        _list.SelectedIndexChanged += (_, _) => UpdateDetail();
        leftPanel.Controls.Add(_list, 0, 1);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        leftPanel.Controls.Add(btnPanel, 0, 2);

        _btnClose.Text = "关闭";
        _btnClose.AutoSize = true;
        _btnClose.Click += (_, _) => Close();
        btnPanel.Controls.Add(_btnClose);

        _btnRunSelected.Text = "执行勾选命令";
        _btnRunSelected.AutoSize = true;
        _btnRunSelected.Click += async (_, _) => await RunSelectedCommandsAsync();
        btnPanel.Controls.Add(_btnRunSelected);

        _btnSaveSelected.Text = "保存到右侧";
        _btnSaveSelected.AutoSize = true;
        _btnSaveSelected.Click += (_, _) => SaveSelectedCommands();
        btnPanel.Controls.Add(_btnSaveSelected);

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Panel2.Controls.Add(rightPanel);

        var lblDetail = new Label
        {
            Dock = DockStyle.Fill,
            Text = "  命令详情",
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        rightPanel.Controls.Add(lblDetail, 0, 0);

        _txtDetail.Dock = DockStyle.Fill;
        _txtDetail.Multiline = true;
        _txtDetail.ReadOnly = true;
        _txtDetail.ScrollBars = ScrollBars.Vertical;
        _txtDetail.Font = new System.Drawing.Font("Cascadia Mono", 10);
        rightPanel.Controls.Add(_txtDetail, 0, 1);

        var lblOutput = new Label
        {
            Dock = DockStyle.Fill,
            Text = "  执行输出",
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        rightPanel.Controls.Add(lblOutput, 0, 2);

        _txtOutput.Dock = DockStyle.Fill;
        _txtOutput.Multiline = true;
        _txtOutput.ReadOnly = true;
        _txtOutput.ScrollBars = ScrollBars.Both;
        _txtOutput.Font = new System.Drawing.Font("Cascadia Mono", 10);
        rightPanel.Controls.Add(_txtOutput, 0, 3);
    }

    /// <summary>
    /// 加载命令列表到左侧勾选框。
    /// </summary>
    private void LoadCommands()
    {
        _list.Items.Clear();
        for (int i = 0; i < _commands.Count; i++)
        {
            var command = _commands[i];
            var title = string.IsNullOrWhiteSpace(command.Name)
                ? command.Command
                : command.Name;
            if (title.Length > 50) title = title[..50] + "...";
            var prefix = command.Optional ? "[可选]" : "[必需]";
            _list.Items.Add($"{i + 1}. {prefix} {title}", !command.Optional);
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// 刷新右侧命令详情。
    /// </summary>
    private void UpdateDetail()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _commands.Count)
        {
            _txtDetail.Text = "";
            return;
        }

        var cmd = _commands[_list.SelectedIndex];
        var workingDir = ResolveWorkingDirectory(cmd);
        _txtDetail.Text =
            $"名称：{cmd.Name}\r\n" +
            $"用途：{cmd.Reason}\r\n" +
            $"Shell：{cmd.Shell}\r\n" +
            $"工作目录：{workingDir}\r\n" +
            $"可选：{cmd.Optional}\r\n\r\n" +
            cmd.Command;
    }

    /// <summary>
    /// 解析命令的实际工作目录。
    /// </summary>
    private string ResolveWorkingDirectory(AiCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.WorkingDirectory) || command.WorkingDirectory == ".")
            return _projectRoot;

        return Path.GetFullPath(Path.Combine(_projectRoot, command.WorkingDirectory));
    }

    /// <summary>
    /// 追加一行输出到日志框。
    /// </summary>
    private void AppendOutput(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendOutput), text);
            return;
        }

        _txtOutput.AppendText(text + Environment.NewLine);
    }

    /// <summary>
    /// 顺序执行勾选的命令。
    /// </summary>
    private async Task RunSelectedCommandsAsync()
    {
        if (_isRunning) return;

        var selected = _list.CheckedIndices.Cast<int>().Select(i => _commands[i]).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先勾选至少一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"将执行 {selected.Count} 条 AI 返回的命令，是否继续？",
            "确认执行",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _isRunning = true;
        _btnRunSelected.Enabled = false;
        _txtOutput.Clear();

        try
        {
            foreach (var command in selected)
            {
                await RunOneCommandAsync(command);
            }
        }
        finally
        {
            _isRunning = false;
            _btnRunSelected.Enabled = true;
        }
    }

    /// <summary>
    /// 把勾选命令回传给主界面，保存到右侧收藏区。
    /// </summary>
    private void SaveSelectedCommands()
    {
        if (_saveSelectedCallback == null)
        {
            MessageBox.Show(this, "当前窗口未连接保存入口。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _list.CheckedIndices.Cast<int>().Select(i => _commands[i]).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先勾选至少一条命令。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _saveSelectedCallback(selected);
        MessageBox.Show(this, $"已保存 {selected.Count} 条命令到右侧收藏区。", "已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// 执行单条 AI 命令并收集输出。
    /// </summary>
    private async Task RunOneCommandAsync(AiCommand command)
    {
        var workingDirectory = ResolveWorkingDirectory(command);
        AppendOutput(new string('=', 60));
        AppendOutput($"[命令] {command.Name}");
        AppendOutput($"[用途] {command.Reason}");
        AppendOutput($"[目录] {workingDirectory}");
        AppendOutput($"[Shell] {command.Shell}");
        AppendOutput(command.Command);

        var shell = (command.Shell ?? "powershell").Trim().ToLowerInvariant();
        
        // 自动探测并修正：如果明确包含 cmd 特有的语法，强制切换为 cmd 模式
        if (shell == "powershell" && (command.Command.Contains("&&") || command.Command.Contains("cd /d ")))
        {
            shell = "cmd";
        }

        ProcessStartInfo psi;
        if (shell == "cmd" || shell == "bat" || shell == "batch")
        {
            psi = new ProcessStartInfo("cmd.exe", "/c " + command.Command);
        }
        else
        {
            var script = "& {\n" + command.Command + "\n}\n";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded);
        }

        psi.WorkingDirectory = workingDirectory;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) AppendOutput(e.Data); };
        process.ErrorDataReceived += (_, e) => { 
            if (e.Data != null) 
            {
                // 过滤掉 PowerShell 恼人的内部 XML 错误格式头
                if (e.Data.StartsWith("#< CLIXML") || e.Data.StartsWith("<Objs Version=")) return;
                // 简单清理 XML 标签
                var cleanData = System.Text.RegularExpressions.Regex.Replace(e.Data, "<.*?>", "");
                cleanData = cleanData.Replace("_x000D__x000A_", "").Replace("&amp;", "&");
                if (!string.IsNullOrWhiteSpace(cleanData))
                {
                    AppendOutput(cleanData);
                }
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            AppendOutput($"[退出码] {process.ExitCode}");
        }
        catch (Exception ex)
        {
            AppendOutput("[执行失败] " + ex.Message);
        }
    }
}
