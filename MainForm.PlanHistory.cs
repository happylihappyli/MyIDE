using System;
using System.Linq;
using System.Windows.Forms;
using MyIDE.Models;

namespace MyIDE;

public partial class MainForm
{
    /// <summary>
    /// 根据当前计划文本生成一个适合列表展示的短标题。
    /// </summary>
    private static string BuildPlanHistoryTitle(string planText)
    {
        var lines = (planText ?? "")
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0) return "未命名计划";

        var title = lines[0];
        if (title.Length > 40) title = title[..40] + "...";
        return title;
    }

    /// <summary>
    /// 保存当前计划到右侧历史区，并通过节流避免频繁生成碎片版本。
    /// </summary>
    private void SaveCurrentPlanToHistory(bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;

        var planText = (_txtPlan.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(planText)) return;

        var now = DateTime.Now;
        if (!force)
        {
            if (string.Equals(planText, _lastSavedPlanHistoryText, StringComparison.Ordinal)) return;
            if ((now - _lastPlanHistorySavedAt).TotalSeconds < 15) return;
        }

        var saved = _settings.SavePlanHistoryEntry(new SavedPlanHistory
        {
            ProjectRoot = _projectRoot,
            Title = BuildPlanHistoryTitle(planText),
            Text = planText
        });

        _lastSavedPlanHistoryText = saved.Text;
        _lastPlanHistorySavedAt = now;
        RefreshSavedPlanPanel();
        var index = _currentSavedPlans.FindIndex(c => string.Equals(c.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _lstSavedPlans.SelectedIndex = index;
    }

    /// <summary>
    /// 刷新右侧计划历史区。
    /// </summary>
    private void RefreshSavedPlanPanel()
    {
        var previousSelectedId = GetSelectedSavedPlan()?.Id;
        _lstSavedPlans.Items.Clear();
        _currentSavedPlans.Clear();
        _txtSavedPlanDetail.Clear();

        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            _lblSavedPlansSummary.Text = "  未打开项目";
            UpdateRightPanelHeaders();
            return;
        }

        var keyword = (_txtSavedPlanSearch.Text ?? "").Trim();
        var allItems = _settings.GetSavedPlanHistory(_projectRoot);
        _currentSavedPlans = allItems
            .Where(item =>
                string.IsNullOrWhiteSpace(keyword) ||
                item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in _currentSavedPlans)
        {
            _lstSavedPlans.Items.Add(BuildSavedPlanListText(item));
        }

        var pinnedCount = allItems.Count(item => item.IsPinned);
        _lblSavedPlansSummary.Text = $"  共 {allItems.Count} 条，当前显示 {_currentSavedPlans.Count} 条，置顶 {pinnedCount} 条";
        if (_lstSavedPlans.Items.Count > 0)
        {
            var restoredIndex = !string.IsNullOrWhiteSpace(previousSelectedId)
                ? _currentSavedPlans.FindIndex(c => string.Equals(c.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
                : -1;
            _lstSavedPlans.SelectedIndex = restoredIndex >= 0 ? restoredIndex : 0;
        }
        else
        {
            UpdateSavedPlanDetail();
        }

        UpdateRightPanelHeaders();
    }

    /// <summary>
    /// 刷新右侧计划历史详情。
    /// </summary>
    private void UpdateSavedPlanDetail()
    {
        var item = GetSelectedSavedPlan();
        if (item == null)
        {
            _txtSavedPlanDetail.Text = "这里会显示计划标题、更新时间和完整内容。";
            _btnPinSavedPlan.Text = "置顶";
            return;
        }

        _txtSavedPlanDetail.Text =
            $"标题：{(string.IsNullOrWhiteSpace(item.Title) ? "未命名计划" : item.Title)}\r\n" +
            $"标签：{(item.IsPinned ? "已置顶" : "普通历史")}\r\n" +
            $"时间：{item.UpdatedAt:yyyy-MM-dd HH:mm:ss}\r\n" +
            $"长度：{item.Text.Length} 字符\r\n\r\n" +
            item.Text;
        _btnPinSavedPlan.Text = item.IsPinned ? "取消置顶" : "置顶";
    }

    /// <summary>
    /// 把右侧选中的计划历史回填到计划编辑区。
    /// </summary>
    private void UseSelectedSavedPlan()
    {
        var item = GetSelectedSavedPlan();
        if (item == null)
        {
            Warn("请先在右侧选择一条计划历史");
            return;
        }

        _txtPlan.Text = item.Text;
        _txtPlan.SelectionStart = _txtPlan.TextLength;
        _txtPlan.SelectionLength = 0;
        _txtPlan.Focus();
        _lblStatus.Text = "● 已回填计划";
        _lblStatus.ForeColor = Accent;
        Log($"✔ 已回填计划历史：{(string.IsNullOrWhiteSpace(item.Title) ? "未命名计划" : item.Title)}");
    }

    /// <summary>
    /// 复制右侧选中的计划历史文本。
    /// </summary>
    private void CopySelectedSavedPlan()
    {
        var item = GetSelectedSavedPlan();
        if (item == null)
        {
            Warn("请先在右侧选择一条计划历史");
            return;
        }

        try
        {
            Clipboard.SetText(item.Text);
            Log($"✔ 已复制计划历史：{(string.IsNullOrWhiteSpace(item.Title) ? "未命名计划" : item.Title)}");
        }
        catch (Exception ex)
        {
            Warn("复制计划失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 删除右侧选中的计划历史记录。
    /// </summary>
    private void DeleteSelectedSavedPlan()
    {
        var item = GetSelectedSavedPlan();
        if (item == null)
        {
            Warn("请先在右侧选择一条计划历史");
            return;
        }

        _settings.RemoveSavedPlanHistory(item.Id);
        RefreshSavedPlanPanel();
        Log($"✔ 已删除计划历史：{(string.IsNullOrWhiteSpace(item.Title) ? "未命名计划" : item.Title)}");
    }

    /// <summary>
    /// 切换右侧选中计划历史的置顶状态。
    /// </summary>
    private void ToggleSelectedSavedPlanPinned()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;

        var item = GetSelectedSavedPlan();
        if (item == null)
        {
            Warn("请先在右侧选择一条计划历史");
            return;
        }

        var nextPinned = !item.IsPinned;
        _settings.SetSavedPlanPinned(_projectRoot, item.Id, nextPinned);
        RefreshSavedPlanPanel();
        var index = _currentSavedPlans.FindIndex(c => string.Equals(c.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _lstSavedPlans.SelectedIndex = index;
        Log(nextPinned
            ? $"✔ 已置顶计划历史：{(string.IsNullOrWhiteSpace(item.Title) ? "未命名计划" : item.Title)}"
            : $"✔ 已取消置顶计划历史：{(string.IsNullOrWhiteSpace(item.Title) ? "未命名计划" : item.Title)}");
    }

    /// <summary>
    /// 右键计划历史时先自动选中当前项，避免菜单作用到旧选择。
    /// </summary>
    private void SavedPlanList_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        var index = _lstSavedPlans.IndexFromPoint(e.Location);
        if (index >= 0)
        {
            _lstSavedPlans.SelectedIndex = index;
        }
    }

    /// <summary>
    /// 在计划历史列表中按快捷键时，支持快速复制和回填。
    /// </summary>
    private void SavedPlanList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            e.SuppressKeyPress = true;
            CopySelectedSavedPlan();
            return;
        }

        if (e.KeyCode != Keys.Enter) return;

        e.SuppressKeyPress = true;
        UseSelectedSavedPlan();
    }
}
