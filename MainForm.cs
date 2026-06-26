using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyIDE.Forms;
using MyIDE.Models;
using MyIDE.Services;

namespace MyIDE;

/// <summary>
/// 主窗体（v2）：暗色主题、标签页布局、差异预览、撤销栈
/// </summary>
public partial class MainForm : Form
{
    private void ShowSettings()
    {
        using var form = new MyIDE.Forms.SettingsForm(_settings);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            Log("✔ 系统设置已更新。");
        }
    }











    private List<string> GetCheckedFiles()
    {
        var list = new List<string>();
        if (_projectRoot == null) return list;

        // 1. 获取树中勾选的文件
        void Traverse(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Checked && n.Tag is string p && File.Exists(p))
                {
                    if (ShouldExcludeFromPrompt(p)) continue;
                    var rel = Path.GetRelativePath(_projectRoot, p).Replace('\\', '/');
                    if (!list.Contains(rel)) list.Add(rel);
                }
                Traverse(n.Nodes);
            }
        }
        Traverse(_tree.Nodes);

        // 2. 获取代码编辑器中当前打开的文件
        foreach (TabPage tab in _editorTabs.TabPages)
        {
            if (tab.Tag is string p && File.Exists(p))
            {
                if (ShouldExcludeFromPrompt(p)) continue;
                var rel = Path.GetRelativePath(_projectRoot, p).Replace('\\', '/');
                if (!list.Contains(rel)) list.Add(rel);
            }
        }

        // 3. 如果还是没有，获取当前在树中选中的文件
        if (list.Count == 0 && GetCurrentSelectedFile() is string sel && !list.Contains(sel))
        {
            var p = Path.Combine(_projectRoot, sel);
            if (!ShouldExcludeFromPrompt(p))
            {
                list.Add(sel);
            }
        }

        return list;
    }

    private string? GetCurrentSelectedFile()
    {
        if (_tree.SelectedNode?.Tag is string p && File.Exists(p) && _projectRoot != null && !ShouldExcludeFromPrompt(p))
            return Path.GetRelativePath(_projectRoot, p).Replace('\\', '/');
        return null;
    }

    /// <summary>
    /// 判断某个文件是否应从提示词上下文中排除。
    /// </summary>
    private static bool ShouldExcludeFromPrompt(string path)
    {
        return path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".dblite", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase);
    }









    /// <summary>
    /// 尝试将文本直接发送到本地 AI 浏览器桥接服务。
    /// </summary>
    private async Task<AiBrowserSendResult> TrySendToAiBrowserAsync(string text, bool autoSend, string source)
    {
        try
        {
            var payload = new AiBrowserSendRequest
            {
                Text = text,
                AutoSend = autoSend,
                Source = source
            };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await AiBrowserHttpClient.PostAsync("http://127.0.0.1:18888/api/ai/send", content);
            var responseText = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AiBrowserSendResult>(responseText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result != null)
            {
                return result;
            }

            return new AiBrowserSendResult
            {
                Ok = response.IsSuccessStatusCode,
                Reason = "script_result_empty",
                Message = "浏览器返回了空结果"
            };
        }
        catch (Exception ex)
        {
            return new AiBrowserSendResult
            {
                Ok = false,
                Reason = "client_exception",
                Message = ex.Message
            };
        }
    }











    private void DoUndo()
    {
        if (_undo.UndoCount == 0) { Warn("没有可撤销的操作"); return; }
        var snap = _undo.Undo();
        if (snap != null)
        {
            RefreshOpenEditors();
            Log($"↩ 已撤销：{snap.Description}（恢复 {snap.Files.Count} 个文件）");
            UpdateUndoLabel();
            _lblStatus.Text = "● 已撤销";
            _lblStatus.ForeColor = Warning;
        }
    }

    private void UpdateUndoLabel() => _lblUndo.Text = $"可撤销: {_undo.UndoCount}";

    private void ShowAbout()
    {
        MessageBox.Show(this,
            "MyIDE · AI 代码修改助手  v0.2\n\n" +
            "工作流：\n" +
            "  1. 打开目录（左侧会显示文件树）\n" +
            "  2. 双击文件，在上方代码查看区打开源码标签页\n" +
            "  3. 下方在「计划」里写要 AI 做什么\n" +
            "  4. 点「生成提示词」或「复制提示词」发给 AI\n" +
            "  5. 把 AI 返回的 Patch/命令内容粘到「AI 返回内容」\n" +
            "  6. 点「预览 Diff」查看修改，再点「应用」落盘\n" +
            "  7. 如果返回内容里还带有 commands，会在应用后弹出命令执行窗口\n" +
            "  8. 应用或撤销后，已打开的代码页会自动刷新\n\n" +
            "Patch 协议（提示词里已自动注入）：\n" +
            "  【补丁】区段使用 Add File / Update File / @@ / Begin Patch / End Patch\n" +
            "  新文件内容直接写文本，不需要 JSON 转义\n" +
            "  【命令】区段使用 JSON 数组\n" +
            "  commands: 应用后可执行的命令列表",
            "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }







    /// <summary>暗色菜单渲染器</summary>
    private class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
            var bg = e.Item.Selected || e.Item.Pressed ? Color.FromArgb(60, 60, 60) : BgHeader;
            using var b = new SolidBrush(bg);
            e.Graphics.FillRectangle(b, rect);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // 强制设置菜单项文字颜色为浅色
            e.TextColor = FgText;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
        {
            // 不画工具栏按钮的默认背景
        }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.FillRectangle(new SolidBrush(BgHeader), e.AffectedBounds);
        }
    }
}
