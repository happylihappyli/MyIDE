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
    /// 构建中间上方的代码查看区域，提供类似编辑器的多标签浏览体验。
    /// </summary>
    private Control BuildEditorPanel()
    {
        var editorPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BgPanel,
            Margin = new Padding(0, 0, 0, 8),
        };
        editorPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        editorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblEditor.Dock = DockStyle.Fill;
        _lblEditor.TextAlign = ContentAlignment.MiddleLeft;
        _lblEditor.BackColor = BgHeader;
        _lblEditor.ForeColor = FgText;
        _lblEditor.Font = new Font("Segoe UI", 9, FontStyle.Bold);

        _editorTabs.Dock = DockStyle.Fill;
        _editorTabs.BackColor = BgDark;
        _editorTabs.ForeColor = FgText;
        _editorTabs.Padding = new Point(14, 4);

        editorPanel.Controls.Add(_lblEditor, 0, 0);
        editorPanel.Controls.Add(_editorTabs, 0, 1);

        ResetEditorTabs();
        return editorPanel;
    }

    /// <summary>
    /// 重置代码查看区为空欢迎页。
    /// </summary>
    private void ResetEditorTabs()
    {
        _editorTabs.TabPages.Clear();
        _editorTabs.TabPages.Add(CreateWelcomeTab());
        _editorTabs.SelectedIndex = 0;
        UpdateEditorHeader();
    }

    /// <summary>
    /// 创建代码查看区的欢迎页。
    /// </summary>
    private TabPage CreateWelcomeTab()
    {
        var tab = new TabPage("开始") { BackColor = BgDark };
        var box = CreateEditorBox();
        box.ReadOnly = true;
        box.Text =
            "欢迎使用 MyIDE 代码编辑器\r\n\r\n" +
            "1. 在左侧文件树单击或双击文件\r\n" +
            "2. 中间上方会像 VS Code 一样打开代码标签页\r\n" +
            "3. 现在支持直接修改代码，修改后按 Ctrl+S 即可保存！\r\n" +
            "4. 下方可以继续写计划，或粘贴 AI 返回的 Patch/命令内容\r\n\r\n" +
            "提示：可以直接在编辑器中编写代码了。";
        tab.Controls.Add(box);
        return tab;
    }

    /// <summary>
    /// 创建统一风格的代码查看控件。
    /// </summary>
    private RichTextBox CreateEditorBox()
    {
        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = false,
            BorderStyle = BorderStyle.None,
            BackColor = BgDark,
            ForeColor = FgText,
            Font = new Font("Cascadia Mono", 10),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            DetectUrls = false,
            HideSelection = false,
            AcceptsTab = true,
        };
        box.Click += UpdateCursorPos;
        box.KeyUp += UpdateCursorPos;
        box.SelectionChanged += UpdateCursorPos;
        box.KeyDown += EditorBox_KeyDown;
        box.TextChanged += EditorBox_TextChanged;
        return box;
    }

    private void EditorBox_TextChanged(object? sender, EventArgs e)
    {
        if (sender is RichTextBox box && box.Parent is TabPage tab && tab.Tag is string path)
        {
            if (!tab.Text.EndsWith("*"))
            {
                tab.Text = Path.GetFileName(path) + " *";
            }
        }
    }

    private void EditorBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            SaveCurrentEditor();
        }
    }

    /// <summary>
    /// 获取当前活动的代码编辑标签页和编辑框，供保存、另存等操作复用。
    /// </summary>
    private bool TryGetCurrentEditorContext(out TabPage? tab, out RichTextBox? editor, out string? path)
    {
        tab = _editorTabs.SelectedTab;
        editor = tab?.Controls.OfType<RichTextBox>().FirstOrDefault();
        path = tab?.Tag as string;

        return tab != null && editor != null && tab.Tag != null;
    }

    /// <summary>
    /// 将指定编辑器标签页内容保存到目标文件，并同步刷新标签页状态。
    /// </summary>
    private bool SaveEditorTabToPath(TabPage tab, RichTextBox editor, string targetPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(targetPath, editor.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            tab.Tag = targetPath;
            tab.Text = Path.GetFileName(targetPath);
            tab.ToolTipText = _projectRoot == null
                ? targetPath
                : Path.GetRelativePath(_projectRoot, targetPath).Replace('\\', '/');

            Log($"✔ 已保存文件：{targetPath}");
            _lblStatus.Text = "● 已保存";
            _lblStatus.ForeColor = Success;

            if (!string.IsNullOrWhiteSpace(_projectRoot))
            {
                RefreshProjectTree();
                OpenFileInEditor(targetPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            Warn($"保存失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存当前正在编辑的文件；如果当前页没有有效文件路径，则转为“另存为”。
    /// </summary>
    private void SaveCurrentEditor()
    {
        if (!TryGetCurrentEditorContext(out var tab, out var editor, out var path) || tab == null || editor == null)
        {
            Warn("当前没有可保存的代码页");
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            SaveCurrentEditorAs();
            return;
        }

        SaveEditorTabToPath(tab, editor, path);
    }

    /// <summary>
    /// 将当前编辑器内容另存为新文件，并在保存后更新文件树和标签页路径。
    /// </summary>
    private void SaveCurrentEditorAs()
    {
        if (!TryGetCurrentEditorContext(out var tab, out var editor, out var currentPath) || tab == null || editor == null)
        {
            Warn("当前没有可另存的代码页");
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "另存为",
            Filter = "文本文件|*.txt|C# 文件|*.cs|C++ 文件|*.cpp;*.h|所有文件|*.*",
            OverwritePrompt = true,
            FileName = string.IsNullOrWhiteSpace(currentPath) ? "new_file.txt" : Path.GetFileName(currentPath)
        };

        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            dlg.InitialDirectory = Path.GetDirectoryName(currentPath);
        }
        else if (!string.IsNullOrWhiteSpace(_projectRoot))
        {
            dlg.InitialDirectory = _projectRoot;
        }

        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FileName))
        {
            return;
        }

        SaveEditorTabToPath(tab, editor, dlg.FileName);
    }

    /// <summary>
    /// 根据当前打开的代码标签页刷新顶部标题。
    /// </summary>
    private void UpdateEditorHeader()
    {
        if (_editorTabs.SelectedTab?.Tag is string fullPath && _projectRoot != null)
        {
            var rel = Path.GetRelativePath(_projectRoot, fullPath).Replace('\\', '/');
            _lblEditor.Text = $"  </>  代码查看 · {rel}";
            return;
        }

        _lblEditor.Text = "  </>  代码查看 · 双击左侧文件打开";
    }

    /// <summary>
    /// 在代码查看区打开指定文件，若已打开则直接切换到对应标签页。
    /// </summary>
    private void OpenFileInEditor(string fullPath)
    {
        foreach (TabPage tab in _editorTabs.TabPages)
        {
            if (string.Equals(tab.Tag as string, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                _editorTabs.SelectedTab = tab;
                UpdateEditorHeader();
                UpdateCursorPos(this, EventArgs.Empty);
                return;
            }
        }

        if (_editorTabs.TabPages.Count == 1 && _editorTabs.TabPages[0].Tag == null)
            _editorTabs.TabPages.Clear();

        var rel = _projectRoot == null
            ? Path.GetFileName(fullPath)
            : Path.GetRelativePath(_projectRoot, fullPath).Replace('\\', '/');

        var tabPage = new TabPage(Path.GetFileName(fullPath))
        {
            BackColor = BgDark,
            Tag = fullPath,
            ToolTipText = rel,
        };
        var box = CreateEditorBox();
        box.Text = BuildEditorText(fullPath);
        tabPage.Controls.Add(box);
        _editorTabs.TabPages.Add(tabPage);
        _editorTabs.SelectedTab = tabPage;

        UpdateEditorHeader();
        _lblStatus.Text = "● 已打开代码查看";
        _lblStatus.ForeColor = Accent;
    }

    /// <summary>
    /// 关闭当前代码标签页，保留欢迎页作为空状态。
    /// </summary>
    private void CloseCurrentEditorTab()
    {
        var current = _editorTabs.SelectedTab;
        if (current == null || current.Tag == null)
        {
            Warn("当前没有可关闭的代码页");
            return;
        }

        _editorTabs.TabPages.Remove(current);
        current.Dispose();
        if (_editorTabs.TabPages.Count == 0)
            ResetEditorTabs();

        UpdateEditorHeader();
        UpdateCursorPos(this, EventArgs.Empty);
    }

    /// <summary>
    /// 刷新所有已打开的代码标签页内容，确保界面与磁盘文件一致。
    /// </summary>
    private void RefreshOpenEditors()
    {
        foreach (TabPage tab in _editorTabs.TabPages)
        {
            if (tab.Tag is not string fullPath) continue;
            if (tab.Controls.OfType<RichTextBox>().FirstOrDefault() is not RichTextBox box) continue;

            box.Text = BuildEditorText(fullPath);
            tab.Text = Path.GetFileName(fullPath);
        }

        UpdateEditorHeader();
        UpdateCursorPos(this, EventArgs.Empty);
    }

    /// <summary>
    /// 读取文件内容并返回文本（不再加行号，以支持直接编辑）。
    /// </summary>
    private string BuildEditorText(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
                return "";

            var info = new FileInfo(fullPath);
            if (info.Length > 1024 * 1024)
                return $"// 文件过大，暂不直接展示。\r\n// 路径：{fullPath}\r\n// 大小：{info.Length / 1024} KB";

            return File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            return $"// 读取文件失败：{ex.Message}";
        }
    }

    private void UpdateCursorPos(object? sender, EventArgs e)
    {
        var txt = GetActiveTextControl();
        if (txt == null)
        {
            _lblCursor.Text = "行: 1, 列: 1";
            return;
        }

        if (txt.TextLength == 0)
        {
            _lblCursor.Text = "行: 1, 列: 1";
            return;
        }
        int index = Math.Min(txt.SelectionStart, txt.TextLength);
        int line = txt.GetLineFromCharIndex(index);
        int firstChar = txt.GetFirstCharIndexFromLine(line);
        int col = index - firstChar;
        _lblCursor.Text = $"行: {line + 1}, 列: {col + 1}";
    }
}
