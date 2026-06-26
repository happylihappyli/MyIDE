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
    private int _transientStatusVersion;

    private void Log(string msg)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Log), msg);
            return;
        }
        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        if (_chkAutoScrollLog.Checked)
        {
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }
        if (_bottomOutputTabs.SelectedTab != _tabOperationLog)
        {
            _pendingOperationLogCount++;
        }
        UpdateBottomOutputTabCaptions();
        UpdateRightPanelHeaders();
    }

    private void Warn(string msg)
    {
        Log($"⚠ {msg}");
        MessageBox.Show(this, msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// 在状态栏显示一条会自动恢复的临时提示，避免使用阻塞式弹窗打断当前操作。
    /// </summary>
    private async void ShowTransientStatus(string message, Color? color = null, int durationMs = 2200)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string, Color?, int>(ShowTransientStatus), message, color, durationMs);
            return;
        }

        var currentVersion = ++_transientStatusVersion;
        var previousText = _lblStatus.Text;
        var previousColor = _lblStatus.ForeColor;

        _lblStatus.Text = message;
        _lblStatus.ForeColor = color ?? Accent;

        try
        {
            await Task.Delay(durationMs);
        }
        catch
        {
            return;
        }

        if (IsDisposed || !IsHandleCreated) return;
        if (currentVersion != _transientStatusVersion) return;
        if (!string.Equals(_lblStatus.Text, message, StringComparison.Ordinal)) return;

        _lblStatus.Text = previousText;
        _lblStatus.ForeColor = previousColor;
    }

    /// <summary>
    /// 清空右侧日志内容，并同步刷新标题数量提示。
    /// </summary>
    private void ClearLogPanel()
    {
        _txtLog.Clear();
        _pendingOperationLogCount = 0;
        UpdateBottomOutputTabCaptions();
        UpdateRightPanelHeaders();
    }

    /// <summary>
    /// 统计右侧日志中的有效条目数，用于显示标题徽标。
    /// </summary>
    private int GetLogEntryCount()
    {
        return _txtLog.Lines.Count(line => !string.IsNullOrWhiteSpace(line));
    }

    /// <summary>
    /// 切换到命令输出标签页，让运行结果更像终端一样始终出现在前面。
    /// </summary>
    private void ActivateRunOutputTab()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(ActivateRunOutputTab));
            return;
        }

        if (_bottomOutputTabs.TabPages.Count > 0 && _bottomOutputTabs.SelectedTab != _tabRunOutput)
        {
            _bottomOutputTabs.SelectedTab = _tabRunOutput;
        }
    }

    /// <summary>
    /// 当用户切到日志页时清除未读计数，保持标签标题和视觉状态同步。
    /// </summary>
    private void HandleBottomOutputTabChanged()
    {
        if (_bottomOutputTabs.SelectedTab == _tabOperationLog && _pendingOperationLogCount != 0)
        {
            _pendingOperationLogCount = 0;
            UpdateBottomOutputTabCaptions();
        }
    }

    /// <summary>
    /// 根据当前日志数量和未读数量刷新下方两个标签页标题。
    /// </summary>
    private void UpdateBottomOutputTabCaptions()
    {
        var logCount = GetLogEntryCount();
        _tabRunOutput.Text = HasActiveRunProcess()
            ? "命令输出 ● 运行中"
            : "命令输出";
        _tabOperationLog.Text = _pendingOperationLogCount > 0
            ? $"操作日志 [{logCount}] •{_pendingOperationLogCount}"
            : $"操作日志 [{logCount}]";

        if (IsHandleCreated)
        {
            _bottomOutputTabs.Invalidate();
        }
    }

    /// <summary>
    /// 以深色主题绘制下方标签页，突出当前页和未读日志状态，风格更接近 Cursor/Trae。
    /// </summary>
    private void BottomOutputTabs_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _bottomOutputTabs.TabPages.Count) return;

        var tab = _bottomOutputTabs.TabPages[e.Index];
        var bounds = e.Bounds;
        var isSelected = e.Index == _bottomOutputTabs.SelectedIndex;
        var isLogTab = tab == _tabOperationLog;
        var hasPendingLogs = isLogTab && _pendingOperationLogCount > 0;

        var backColor = isSelected
            ? Color.FromArgb(45, 45, 48)
            : hasPendingLogs
                ? Color.FromArgb(52, 64, 84)
                : Color.FromArgb(37, 37, 38);
        var foreColor = isSelected
            ? Color.White
            : hasPendingLogs
                ? Accent
                : FgMuted;

        using var backBrush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(backBrush, bounds);

        var textRect = Rectangle.Inflate(bounds, -10, -3);
        TextRenderer.DrawText(
            e.Graphics,
            tab.Text,
            new Font("Segoe UI", isSelected ? 9F : 8.8F, isSelected ? FontStyle.Bold : FontStyle.Regular),
            textRect,
            foreColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using var dividerPen = new Pen(Color.FromArgb(60, 60, 60));
        e.Graphics.DrawLine(dividerPen, bounds.Right - 1, bounds.Top + 5, bounds.Right - 1, bounds.Bottom - 5);

        if (isSelected)
        {
            using var accentBrush = new SolidBrush(Accent);
            e.Graphics.FillRectangle(accentBrush, bounds.Left + 8, bounds.Bottom - 3, Math.Max(24, bounds.Width - 16), 3);
        }
        else if (hasPendingLogs)
        {
            using var pendingBrush = new SolidBrush(Accent);
            e.Graphics.FillEllipse(pendingBrush, bounds.Right - 14, bounds.Top + 8, 6, 6);
        }
    }

    /// <summary>
    /// 判断当前是否存在正在执行的底部终端进程。
    /// </summary>
    private bool HasActiveRunProcess()
    {
        lock (_activeRunProcessSync)
        {
            return _activeRunProcess is { HasExited: false };
        }
    }

    /// <summary>
    /// 刷新底部终端区的运行状态显示和按钮可用性。
    /// </summary>
    private void UpdateRunSessionState(string message, Color color, bool isRunning)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string, Color, bool>(UpdateRunSessionState), message, color, isRunning);
            return;
        }

        _lblRunSessionState.Text = message;
        _lblRunSessionState.ForeColor = color;
        _btnStopRunOutput.Enabled = isRunning;
        UpdateBottomOutputTabCaptions();
    }

    /// <summary>
    /// 在启动新进程前登记当前终端任务，避免多个底部命令同时写入同一输出区。
    /// </summary>
    private bool TryAttachActiveRunProcess(System.Diagnostics.Process process, string displayName)
    {
        lock (_activeRunProcessSync)
        {
            if (_activeRunProcess is { HasExited: false })
            {
                return false;
            }

            _activeRunProcess = process;
            _activeRunProcessDisplayName = displayName;
            _activeRunStopRequested = false;
        }

        UpdateRunSessionState($"● 运行中：{displayName}", Accent, true);
        return true;
    }

    /// <summary>
    /// 在进程结束后清理当前终端任务状态，并显示结束原因。
    /// </summary>
    private void DetachActiveRunProcess(System.Diagnostics.Process process, bool timedOut, bool stoppedByUser, int exitCode)
    {
        var displayName = "";
        lock (_activeRunProcessSync)
        {
            if (!ReferenceEquals(_activeRunProcess, process))
            {
                return;
            }

            displayName = _activeRunProcessDisplayName;
            _activeRunProcess = null;
            _activeRunProcessDisplayName = "";
            _activeRunStopRequested = false;
        }

        if (stoppedByUser)
        {
            UpdateRunSessionState("已停止", Warning, false);
            ShowTransientStatus($"● 已停止：{displayName}", Warning);
        }
        else if (timedOut)
        {
            UpdateRunSessionState("执行超时", Error, false);
        }
        else if (exitCode == 0)
        {
            UpdateRunSessionState("空闲", Success, false);
        }
        else
        {
            UpdateRunSessionState("空闲", FgMuted, false);
        }
    }

    /// <summary>
    /// 手动停止当前底部终端进程。
    /// </summary>
    private void StopActiveRunProcess()
    {
        System.Diagnostics.Process? process;
        string displayName;

        lock (_activeRunProcessSync)
        {
            process = _activeRunProcess;
            displayName = _activeRunProcessDisplayName;
            if (process == null || process.HasExited)
            {
                UpdateRunSessionState("空闲", FgMuted, false);
                ShowTransientStatus("● 当前没有正在运行的命令", Warning);
                return;
            }

            _activeRunStopRequested = true;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            AppendRunOutput($"⏹ 已请求停止：{displayName}\n", Warning);
            Log($"⏹ 已请求停止当前命令：{displayName}");
        }
        catch (Exception ex)
        {
            Log($"✖ 停止当前命令失败：{ex.Message}");
            ShowTransientStatus("● 停止失败，请看日志", Error);
        }
    }
}
