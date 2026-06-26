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
    /// 刷新右侧 AI 命令收藏区。
    /// </summary>
    private void RefreshSavedCommandsPanel()
    {
        var previousSelectedId = GetSelectedSavedCommand()?.Id;
        _lstSavedCommands.Items.Clear();
        _currentSavedCommands.Clear();
        _txtSavedCommandDetail.Clear();

        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            _lblSavedCommandsSummary.Text = "  未打开项目";
            UpdateRightPanelHeaders();
            return;
        }

        var keyword = (_txtSavedCommandSearch.Text ?? "").Trim();
        var allCommands = _settings.GetSavedCommands(_projectRoot);
        _currentSavedCommands = allCommands
            .Where(c =>
                string.IsNullOrWhiteSpace(keyword) ||
                c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                c.Reason.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                c.Command.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var command in _currentSavedCommands)
        {
            _lstSavedCommands.Items.Add(BuildSavedCommandListText(command));
        }

        var buildCount = allCommands.Count(c => c.IsDefaultBuild);
        var runCount = allCommands.Count(c => c.IsDefaultRun);
        _lblSavedCommandsSummary.Text = $"  共 {allCommands.Count} 条，当前显示 {_currentSavedCommands.Count} 条，默认编译 {buildCount}，默认运行 {runCount}";

        if (_lstSavedCommands.Items.Count > 0)
        {
            var restoredIndex = !string.IsNullOrWhiteSpace(previousSelectedId)
                ? _currentSavedCommands.FindIndex(c => string.Equals(c.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                : -1;
            _lstSavedCommands.SelectedIndex = restoredIndex >= 0 ? restoredIndex : 0;
        }
        else
            UpdateSavedCommandDetail();

        UpdateRightPanelHeaders();
    }

    /// <summary>
    /// 刷新右侧收藏命令详情，便于像侧边栏一样快速查看命令用途和内容。
    /// </summary>
    private void UpdateSavedCommandDetail()
    {
        var command = GetSelectedSavedCommand();
        if (command == null)
        {
            _txtSavedCommandDetail.Text = "这里会显示收藏命令的用途、目录和完整命令。";
            return;
        }

        var flags = new List<string>();
        if (command.IsDefaultBuild) flags.Add("默认编译");
        if (command.IsDefaultRun) flags.Add("默认运行");
        if (command.Optional) flags.Add("可选");

        _txtSavedCommandDetail.Text =
            $"名称：{(string.IsNullOrWhiteSpace(command.Name) ? "未命名命令" : command.Name)}\r\n" +
            $"标签：{(flags.Count == 0 ? "普通收藏" : string.Join(" / ", flags))}\r\n" +
            $"用途：{(string.IsNullOrWhiteSpace(command.Reason) ? "未填写" : command.Reason)}\r\n" +
            $"Shell：{command.Shell}\r\n" +
            $"目录：{ResolveSavedCommandWorkingDirectory(command)}\r\n\r\n" +
            command.Command;
    }

    /// <summary>
    /// 将 AI 返回的命令保存到右侧收藏区。
    /// </summary>
    private void SaveAiCommandsToSidebar(IReadOnlyList<AiCommand> commands)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot) || commands.Count == 0)
            return;

        var savedCount = 0;
        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Command)) continue;

            _settings.SaveCommand(new SavedAiCommand
            {
                ProjectRoot = _projectRoot,
                Name = command.Name,
                Reason = command.Reason,
                Command = command.Command,
                Shell = string.IsNullOrWhiteSpace(command.Shell) ? "powershell" : command.Shell,
                WorkingDirectory = command.WorkingDirectory,
                Optional = command.Optional
            });
            savedCount++;
        }

        RefreshSavedCommandsPanel();
        Log($"✔ 已保存 {savedCount} 条 AI 命令到右侧收藏区。");
    }

    /// <summary>
    /// 把选中命令设为当前项目的默认编译命令。
    /// </summary>
    private void SetSelectedSavedCommandAsBuild()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;
        var command = GetSelectedSavedCommand();
        if (command == null)
        {
            Warn("请先在右侧选择一条命令");
            return;
        }

        _settings.SetDefaultBuildCommand(_projectRoot, command.Id);
        RefreshSavedCommandsPanel();
        Log($"✔ 已将命令设为默认编译：{command.Name}");
    }

    /// <summary>
    /// 把选中命令设为当前项目的默认运行命令。
    /// </summary>
    private void SetSelectedSavedCommandAsRun()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;
        var command = GetSelectedSavedCommand();
        if (command == null)
        {
            Warn("请先在右侧选择一条命令");
            return;
        }

        _settings.SetDefaultRunCommand(_projectRoot, command.Id);
        RefreshSavedCommandsPanel();
        Log($"✔ 已将命令设为默认运行：{command.Name}");
    }

    /// <summary>
    /// 删除右侧选中的收藏命令。
    /// </summary>
    private void DeleteSelectedSavedCommand()
    {
        var command = GetSelectedSavedCommand();
        if (command == null)
        {
            Warn("请先在右侧选择一条命令");
            return;
        }

        if (MessageBox.Show(this, $"确认删除命令“{command.Name}”吗？", "删除命令", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _settings.RemoveSavedCommand(command.Id);
        RefreshSavedCommandsPanel();
        Log($"✔ 已删除收藏命令：{command.Name}");
    }

    /// <summary>
    /// 复制右侧选中的收藏命令文本。
    /// </summary>
    private void CopySelectedSavedCommand()
    {
        var command = GetSelectedSavedCommand();
        if (command == null)
        {
            Warn("请先在右侧选择一条命令");
            return;
        }

        try
        {
            Clipboard.SetText(command.Command);
            Log($"✔ 已复制收藏命令：{(string.IsNullOrWhiteSpace(command.Name) ? command.Command : command.Name)}");
        }
        catch (Exception ex)
        {
            Warn("复制命令失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 调整右侧收藏命令的顺序并持久化保存。
    /// </summary>
    private void MoveSelectedSavedCommand(int direction)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;
        var command = GetSelectedSavedCommand();
        if (command == null)
        {
            Warn("请先在右侧选择一条命令");
            return;
        }

        if (!_settings.MoveSavedCommand(_projectRoot, command.Id, direction))
            return;

        RefreshSavedCommandsPanel();
        Log($"✔ 已调整命令顺序：{(string.IsNullOrWhiteSpace(command.Name) ? command.Command : command.Name)}");
    }

    /// <summary>
    /// 编辑右侧选中的收藏命令。
    /// </summary>
    private void EditSelectedSavedCommand()
    {
        var command = GetSelectedSavedCommand();
        if (command == null)
        {
            Warn("请先在右侧选择一条命令");
            return;
        }

        using var form = new Forms.EditSavedCommandForm(command);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _settings.Save();
            UpdateSavedCommandDetail();
            // Refresh the item text in listbox
            var index = _lstSavedCommands.SelectedIndex;
            if (index >= 0)
            {
                // ListBox stores strings, so we must build the new string to update the UI
                _lstSavedCommands.Items[index] = BuildSavedCommandListText(command);
            }
            Log($"✔ 已修改收藏命令：{command.Name}");
        }
    }

    /// <summary>
    /// 初始化右侧收藏命令的右键菜单，减少按钮区来回切换。
    /// </summary>
    private void InitializeSavedCommandContextMenu()
    {
        _savedCommandMenu.ShowImageMargin = false;
        _savedCommandMenu.BackColor = BgPanel;
        _savedCommandMenu.ForeColor = FgText;
        _savedCommandMenu.Items.Add("执行", null, async (_, _) => await RunSelectedSavedCommandAsync());
        _savedCommandMenu.Items.Add("编辑", null, (_, _) => EditSelectedSavedCommand());
        _savedCommandMenu.Items.Add("复制命令", null, (_, _) => CopySelectedSavedCommand());
        _savedCommandMenu.Items.Add(new ToolStripSeparator());
        _savedCommandMenu.Items.Add("设为默认编译", null, (_, _) => SetSelectedSavedCommandAsBuild());
        _savedCommandMenu.Items.Add("设为默认运行", null, (_, _) => SetSelectedSavedCommandAsRun());
        _savedCommandMenu.Items.Add(new ToolStripSeparator());
        _savedCommandMenu.Items.Add("上移", null, (_, _) => MoveSelectedSavedCommand(-1));
        _savedCommandMenu.Items.Add("下移", null, (_, _) => MoveSelectedSavedCommand(1));
        _savedCommandMenu.Items.Add(new ToolStripSeparator());
        _savedCommandMenu.Items.Add("删除", null, (_, _) => DeleteSelectedSavedCommand());
        _lstSavedCommands.ContextMenuStrip = _savedCommandMenu;
    }

    /// <summary>
    /// 右键收藏命令时先切换选中项，避免菜单作用到旧选择。
    /// </summary>
    private void SavedCommandsList_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        var index = _lstSavedCommands.IndexFromPoint(e.Location);
        if (index >= 0)
        {
            _lstSavedCommands.SelectedIndex = index;
        }
    }

    /// <summary>
    /// 在收藏命令列表中按回车，直接执行当前选中命令。
    /// </summary>
    private async void SavedCommandsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            e.SuppressKeyPress = true;
            CopySelectedSavedCommand();
            return;
        }

        if (e.KeyCode != Keys.Enter) return;

        e.SuppressKeyPress = true;
        await RunSelectedSavedCommandAsync();
    }

    /// <summary>
    /// 在 AI 返回历史列表中按 Ctrl+C 时，直接复制当前选中内容。
    /// </summary>
    private void SavedJsonList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!(e.Control && e.KeyCode == Keys.C)) return;

        e.SuppressKeyPress = true;
        CopySelectedSavedJson();
    }

    /// <summary>
    /// 立即执行右侧选中的收藏命令。
    /// </summary>
    private async Task RunSelectedSavedCommandAsync()
    {
        var command = GetSelectedSavedCommand();
        if (command == null)
        {
            Warn("请先在右侧选择一条命令");
            return;
        }

        await ExecuteSavedCommandToRunOutputAsync(command, "手动执行", 60_000);
    }

    private async System.Threading.Tasks.Task RunTerminalCommandAsync(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText)) return;
        
        string cwd = string.IsNullOrEmpty(_terminalCwd) ? (_projectRoot ?? AppContext.BaseDirectory) : _terminalCwd;
        ActivateRunOutputTab();
        
        AppendRunOutput($"\n> {commandText}\n", Color.Cyan);
        
        var psi = BuildShellProcessStartInfo("powershell", commandText, cwd);
        
        try
        {
            var result = await RunProcessCaptureAsync(
                psi,
                120_000,
                onStdOutLine: line => AppendRunOutput(line + "\n"),
                onStdErrLine: line => AppendRunOutput(line + "\n", Color.IndianRed),
                displayName: $"命令: {commandText}");
            if (result.StoppedByUser)
            {
                AppendRunOutput("⏹ 命令已手动停止。\n", Warning);
                return;
            }
            if (result.ExitCode != 0)
            {
                AppendRunOutput($"进程退出，代码：{result.ExitCode}\n", Color.Orange);
                await AutoExtractAndSendErrorAsync("运行失败");
            }
        }
        catch (Exception ex)
        {
            if (string.Equals(ex.Message, "当前已有命令正在执行，请先停止或等待完成。", StringComparison.Ordinal))
            {
                AppendRunOutput("⚠ 当前已有命令正在执行，请先停止或等待完成。\n", Warning);
                ShowTransientStatus("● 当前已有命令正在执行", Warning);
                return;
            }
            AppendRunOutput($"执行出错: {ex.Message}\n", Color.Red);
            await AutoExtractAndSendErrorAsync("执行异常");
        }
    }

    /// <summary>
    /// 把文本追加到运行输出页。
    /// </summary>
    private void AppendRunOutput(string text, Color? color = null)
    {
        if (_txtRunOutput.InvokeRequired)
        {
            _txtRunOutput.BeginInvoke(new Action<string, Color?>(AppendRunOutput), text, color);
            return;
        }

        if (color.HasValue)
        {
            _txtRunOutput.SelectionStart = _txtRunOutput.TextLength;
            _txtRunOutput.SelectionLength = 0;
            _txtRunOutput.SelectionColor = color.Value;
        }
        _txtRunOutput.AppendText(text);
        if (color.HasValue)
        {
            _txtRunOutput.SelectionColor = _txtRunOutput.ForeColor;
        }
        if (_chkAutoScrollOutput.Checked)
        {
            _txtRunOutput.ScrollToCaret();
        }
    }

    /// <summary>
    /// 解析收藏命令的绝对工作目录。
    /// </summary>
    private string ResolveSavedCommandWorkingDirectory(SavedAiCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.WorkingDirectory) || command.WorkingDirectory == ".")
            return _projectRoot ?? "";

        return Path.GetFullPath(Path.Combine(_projectRoot ?? "", command.WorkingDirectory));
    }

    /// <summary>
    /// 执行收藏命令，并把输出写到运行输出页。
    /// </summary>
    private async Task<bool> ExecuteSavedCommandToRunOutputAsync(SavedAiCommand command, string stageName, int timeoutMs)
    {
        var workingDirectory = ResolveSavedCommandWorkingDirectory(command);
        ActivateRunOutputTab();
        AppendRunOutput($"[{DateTime.Now:HH:mm:ss}] 使用{stageName}命令：{command.Name}\n");
        AppendRunOutput($"[目录] {workingDirectory}\n");
        AppendRunOutput($"[Shell] {command.Shell}\n");
        AppendRunOutput($"> {command.Command}\n");

        try
        {
            var psi = BuildShellProcessStartInfo(command.Shell, command.Command, workingDirectory);
            var result = await RunProcessCaptureAsync(
                psi,
                timeoutMs,
                onStdOutLine: line => AppendRunOutput(line + "\n"),
                onStdErrLine: line => AppendRunOutput(line + "\n", Color.IndianRed),
                displayName: $"{stageName}: {command.Name}");

            if (result.TimedOut)
            {
                AppendRunOutput($"❌ {stageName}超时：{timeoutMs / 1000} 秒内未完成，已自动终止。\n");
                await AutoExtractAndSendErrorAsync($"{stageName}超时");
                return false;
            }

            if (result.StoppedByUser)
            {
                AppendRunOutput($"⏹ {stageName}已手动停止。\n", Warning);
                return false;
            }

            if (result.ExitCode != 0)
            {
                AppendRunOutput($"❌ {stageName}失败，退出码：{result.ExitCode}\n");
                await AutoExtractAndSendErrorAsync($"{stageName}失败");
                return false;
            }

            AppendRunOutput($"✅ {stageName}成功，退出码：{result.ExitCode}\n");
            Log($"✔ 已执行收藏命令：{command.Name}");
            return true;
        }
        catch (Exception ex)
        {
            if (string.Equals(ex.Message, "当前已有命令正在执行，请先停止或等待完成。", StringComparison.Ordinal))
            {
                AppendRunOutput($"⚠ 当前已有命令正在执行，无法启动{stageName}。\n", Warning);
                ShowTransientStatus("● 当前已有命令正在执行", Warning);
                return false;
            }
            AppendRunOutput($"❌ {stageName}异常：{ex.Message}\n");
            await AutoExtractAndSendErrorAsync($"{stageName}异常");
            return false;
        }
    }

    /// <summary>
    /// 生成右侧收藏命令的列表文本，让默认编译、默认运行更醒目。
    /// </summary>
    private static string BuildSavedCommandListText(SavedAiCommand command)
    {
        var category = command.IsDefaultBuild
            ? "BUILD"
            : command.IsDefaultRun
                ? "RUN"
                : "SAVED";
        var title = string.IsNullOrWhiteSpace(command.Name) ? command.Command : command.Name;
        if (title.Length > 34) title = title[..34] + "...";
        return $"[{category}] {title}";
    }

    /// <summary>
    /// 右键 AI 返回历史时先自动选中当前项，避免菜单作用到旧选择。
    /// </summary>
    private void SavedJsonList_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        var index = _lstSavedJson.IndexFromPoint(e.Location);
        if (index >= 0)
        {
            _lstSavedJson.SelectedIndex = index;
        }
    }

    /// <summary>
    /// 刷新右侧 AI 返回历史区。
    /// </summary>
    private void RefreshSavedJsonPanel()
    {
        var previousSelectedId = GetSelectedSavedJson()?.Id;
        _lstSavedJson.Items.Clear();
        _currentSavedJsonHistory.Clear();
        _txtSavedJsonDetail.Clear();

        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            _lblSavedJsonSummary.Text = "  未打开项目";
            UpdateRightPanelHeaders();
            return;
        }

        var keyword = (_txtSavedJsonSearch.Text ?? "").Trim();
        var allItems = _settings.GetSavedJsonHistory(_projectRoot);
        _currentSavedJsonHistory = allItems
            .Where(item =>
                string.IsNullOrWhiteSpace(keyword) ||
                item.Task.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.JsonText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var item in _currentSavedJsonHistory)
        {
            _lstSavedJson.Items.Add(BuildSavedJsonListText(item));
        }

        var pinnedCount = allItems.Count(item => item.IsPinned);
        _lblSavedJsonSummary.Text = $"  共 {allItems.Count} 条，当前显示 {_currentSavedJsonHistory.Count} 条，置顶 {pinnedCount} 条";
        if (_lstSavedJson.Items.Count > 0)
        {
            var restoredIndex = !string.IsNullOrWhiteSpace(previousSelectedId)
                ? _currentSavedJsonHistory.FindIndex(c => string.Equals(c.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                : -1;
            _lstSavedJson.SelectedIndex = restoredIndex >= 0 ? restoredIndex : 0;
        }
        else
        {
            UpdateSavedJsonDetail();
        }

        UpdateRightPanelHeaders();
    }

    /// <summary>
    /// 刷新右侧 AI 返回详情。
    /// </summary>
    private void UpdateSavedJsonDetail()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            _txtSavedJsonDetail.Text = "这里会显示最近一次 AI 返回内容的任务摘要和完整内容。";
            _btnPinSavedJson.Text = "置顶";
            return;
        }

        _txtSavedJsonDetail.Text =
            $"任务：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}\r\n" +
            $"标签：{(item.IsPinned ? "已置顶" : "普通历史")}\r\n" +
            $"修改：{item.ChangeCount} 个文件\r\n" +
            $"命令：{item.CommandCount} 条\r\n" +
            $"时间：{item.UpdatedAt:yyyy-MM-dd HH:mm:ss}\r\n\r\n" +
            item.JsonText;
        _btnPinSavedJson.Text = item.IsPinned ? "取消置顶" : "置顶";
    }
}
