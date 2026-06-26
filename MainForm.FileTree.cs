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
    private void BtnOpen_Click()
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && Directory.Exists(dlg.SelectedPath))
            LoadDirectory(dlg.SelectedPath);
    }

    private void LoadDirectory(string path)
    {
        _projectRoot = path;
        _lastSidebarSyncedAiJson = "";
        _lastSavedPlanHistoryText = "";
        _lastPlanHistorySavedAt = DateTime.MinValue;
        ResetEditorTabs();
        RefreshSavedPlanPanel();
        RefreshSavedJsonPanel();
        RefreshSavedCommandsPanel();
        try
        {
            PopulateTree(path);
            Log($"✔ 已加载项目目录：{path}");
            _lblProject.Text = "项目根：" + path;
            _lblStatus.Text = "● 就绪";
            _lblStatus.ForeColor = Success;

            _settings.AddRecentDir(path);
            UpdateRecentMenu();
        }
        catch (Exception ex)
        {
            Log($"✖ 加载目录失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 刷新当前项目的文件树，保持左侧面板与磁盘内容同步。
    /// </summary>
    private void RefreshProjectTree()
    {
        if (!string.IsNullOrWhiteSpace(_projectRoot))
        {
            int archivedBakCount = 0;
            try
            {
                archivedBakCount = ArchiveProjectBakFilesOnRefresh();
            }
            catch (Exception ex)
            {
                Log($"⚠ 归档 .bak 文件失败：{ex.Message}");
            }

            PopulateTree(_projectRoot, preserveState: true);
            _lblProject.Text = "项目根：" + _projectRoot;
            _lblStatus.Text = archivedBakCount > 0
                ? $"● 文件树已刷新，并归档 {archivedBakCount} 个 .bak 文件"
                : "● 文件树已刷新";
            _lblStatus.ForeColor = Success;
            if (archivedBakCount > 0)
            {
                ShowTransientStatus($"● 已归档 {archivedBakCount} 个 .bak 文件", Success);
            }
        }
    }

    /// <summary>
    /// 刷新文件树前，将项目中的 .bak 文件移动到统一备份目录，避免备份文件继续堆在项目内。
    /// </summary>
    private int ArchiveProjectBakFilesOnRefresh()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot) || !Directory.Exists(_projectRoot))
        {
            return 0;
        }

        string backupRoot = GetDeletedFilesBackupRoot();
        string archiveRoot = Path.Combine(
            backupRoot,
            "RefreshBak",
            DateTime.Now.ToString("yyyy-MM-dd"),
            Path.GetFileName(_projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        Directory.CreateDirectory(archiveRoot);

        string normalizedProjectRoot = Path.GetFullPath(_projectRoot);
        string normalizedArchiveRoot = Path.GetFullPath(archiveRoot);
        var bakFiles = Directory
            .EnumerateFiles(normalizedProjectRoot, "*.bak", SearchOption.AllDirectories)
            .Where(file => !IsPathUnderDirectory(file, normalizedArchiveRoot))
            .ToList();

        int movedCount = 0;
        foreach (var bakFile in bakFiles)
        {
            try
            {
                string relativePath = Path.GetRelativePath(normalizedProjectRoot, bakFile);
                string relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
                string targetDir = Path.Combine(archiveRoot, relativeDir);
                Directory.CreateDirectory(targetDir);

                string targetPath = CreateUniqueArchivePath(Path.Combine(targetDir, Path.GetFileName(bakFile)));
                File.Move(bakFile, targetPath);
                movedCount++;
                Log($"✔ 已归档 .bak 文件: {bakFile} -> {targetPath}");
            }
            catch (Exception ex)
            {
                Log($"⚠ 归档 .bak 文件失败: {bakFile}，原因：{ex.Message}");
            }
        }

        return movedCount;
    }

    /// <summary>
    /// 获取删除文件与刷新归档共同使用的备份根目录。
    /// </summary>
    private string GetDeletedFilesBackupRoot()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DeletedFilesBackupPath))
        {
            return _settings.DeletedFilesBackupPath;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyIDE", "DeletedFiles");
    }

    /// <summary>
    /// 判断文件路径是否位于指定目录内部，避免重复扫描已经归档出去的 .bak 文件。
    /// </summary>
    private static bool IsPathUnderDirectory(string filePath, string directoryPath)
    {
        string normalizedFile = Path.GetFullPath(filePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedDir = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedFile.StartsWith(
            normalizedDir + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 为归档文件生成一个不冲突的目标路径，避免同名 .bak 被后一次移动覆盖。
    /// </summary>
    private static string CreateUniqueArchivePath(string preferredPath)
    {
        if (!File.Exists(preferredPath))
        {
            return preferredPath;
        }

        string directory = Path.GetDirectoryName(preferredPath) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(preferredPath);
        string extension = Path.GetExtension(preferredPath);
        string timeSuffix = DateTime.Now.ToString("HHmmssfff");

        return Path.Combine(directory, $"{fileName}.{timeSuffix}{extension}");
    }

    /// <summary>
    /// 根据指定项目目录重建左侧文件树，但不重置当前已打开的编辑器标签页。
    /// </summary>
    private void PopulateTree(string path)
    {
        PopulateTree(path, preserveState: false);
    }

    /// <summary>
    /// 根据指定项目目录重建左侧文件树，并按需恢复之前的展开、勾选和选中状态。
    /// </summary>
    private void PopulateTree(string path, bool preserveState)
    {
        (HashSet<string> ExpandedDirectories, HashSet<string> CheckedPaths, string? SelectedPath)? snapshot =
            preserveState ? CaptureTreeStateSnapshot() : null;
        _tree.Nodes.Clear();
        var rootNode = new TreeNode("📁  " + Path.GetFileName(path)) { Tag = path, ForeColor = FgText };
        _tree.Nodes.Add(rootNode);
        AddDirectoryNodes(rootNode, path, depth: 0);
        rootNode.Expand();
        if (snapshot != null)
        {
            RestoreTreeStateSnapshot(rootNode, snapshot.Value);
        }
        else
        {
            _tree.SelectedNode = rootNode;
        }
    }

    /// <summary>
    /// 记录文件树当前已展开目录、勾选节点和选中节点，便于刷新后恢复视觉状态。
    /// </summary>
    private (HashSet<string> ExpandedDirectories, HashSet<string> CheckedPaths, string? SelectedPath) CaptureTreeStateSnapshot()
    {
        var expandedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? selectedPath = _tree.SelectedNode?.Tag as string;

        void Traverse(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is string nodePath)
                {
                    if (Directory.Exists(nodePath) && node.IsExpanded)
                    {
                        expandedDirectories.Add(nodePath);
                    }

                    if (node.Checked)
                    {
                        checkedPaths.Add(nodePath);
                    }
                }

                Traverse(node.Nodes);
            }
        }

        Traverse(_tree.Nodes);
        return (expandedDirectories, checkedPaths, selectedPath);
    }

    /// <summary>
    /// 按之前记录的状态恢复文件树的展开、勾选和选中节点，避免刷新后整棵树被折叠。
    /// </summary>
    private void RestoreTreeStateSnapshot(
        TreeNode rootNode,
        (HashSet<string> ExpandedDirectories, HashSet<string> CheckedPaths, string? SelectedPath) snapshot)
    {
        TreeNode? selectedNode = null;

        void Traverse(TreeNode node)
        {
            if (node.Tag is string nodePath)
            {
                if (snapshot.CheckedPaths.Contains(nodePath))
                {
                    node.Checked = true;
                }

                if (snapshot.ExpandedDirectories.Contains(nodePath))
                {
                    node.Expand();
                }

                if (selectedNode == null && string.Equals(nodePath, snapshot.SelectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    selectedNode = node;
                }
            }

            foreach (TreeNode child in node.Nodes)
            {
                Traverse(child);
            }
        }

        Traverse(rootNode);
        _tree.SelectedNode = selectedNode ?? rootNode;
    }

    private void UpdateRecentMenu()
    {
        _menuRecent.DropDownItems.Clear();
        if (_settings.RecentDirs.Count == 0)
        {
            _menuRecent.DropDownItems.Add(new ToolStripMenuItem("无记录") { Enabled = false });
            return;
        }
        foreach (var dir in _settings.RecentDirs)
        {
            var item = new ToolStripMenuItem(dir);
            item.Click += (_, _) => { if (Directory.Exists(dir)) LoadDirectory(dir); else MessageBox.Show(this, "目录不存在"); };
            _menuRecent.DropDownItems.Add(item);
        }
    }

    private void Tree_AfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (e.Action != TreeViewAction.Unknown && e.Node != null)
        {
            // 如果选中/取消选中了文件夹，递归它的所有子节点
            CheckAllChildNodes(e.Node, e.Node.Checked);
        }
    }

    private void CheckAllChildNodes(TreeNode treeNode, bool nodeChecked)
    {
        foreach (TreeNode node in treeNode.Nodes)
        {
            node.Checked = nodeChecked;
            if (node.Nodes.Count > 0)
            {
                CheckAllChildNodes(node, nodeChecked);
            }
        }
    }

    /// <summary>
    /// 在文件树选中节点时自动预览文件，行为类似 VS Code 的单击预览。
    /// </summary>
    private void Tree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is string path && File.Exists(path))
        {
            OpenFileInEditor(path);
        }
    }

    private void Tree_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node?.Tag is string path && File.Exists(path) && _projectRoot != null)
        {
            var rel = Path.GetRelativePath(_projectRoot, path);
            e.Node.Checked = true;
            OpenFileInEditor(path);
            Log($"已打开文件：{rel}，现在可以在上方查看源码，并继续生成提示词");
        }
    }

    private void Tree_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            var node = _tree.GetNodeAt(e.X, e.Y);
            if (node != null)
            {
                _tree.SelectedNode = node;
            }
        }
    }

    private void RenameSelectedNode()
    {
        if (_tree.SelectedNode?.Tag is string path && File.Exists(path))
        {
            var fileName = Path.GetFileName(path);
            var dir = Path.GetDirectoryName(path);
            if (dir == null) return;

            string input = Microsoft.VisualBasic.Interaction.InputBox("请输入新文件名：", "重命名文件", fileName);
            if (!string.IsNullOrWhiteSpace(input) && input != fileName)
            {
                var newPath = Path.Combine(dir, input);
                try
                {
                    if (File.Exists(newPath))
                    {
                        MessageBox.Show("目标文件已存在！", "重命名失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    
                    // 关闭可能打开的同名编辑器标签
                    foreach (TabPage tab in _editorTabs.TabPages)
                    {
                        if (tab.Tag is string p && string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                        {
                            _editorTabs.TabPages.Remove(tab);
                            break;
                        }
                    }

                    File.Move(path, newPath);
                    Log($"✔ 文件已重命名: {fileName} -> {input}");
                    
                    // 刷新文件树
                    if (_projectRoot != null) LoadDirectory(_projectRoot);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"重命名失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    private void OpenDirectoryInExplorer()
    {
        if (_tree.SelectedNode?.Tag is string path)
        {
            string targetDir = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? _projectRoot ?? "");
            if (Directory.Exists(targetDir))
            {
                System.Diagnostics.Process.Start("explorer.exe", targetDir);
            }
        }
        else if (_projectRoot != null && Directory.Exists(_projectRoot))
        {
            System.Diagnostics.Process.Start("explorer.exe", _projectRoot);
        }
    }

    private void OpenDirectoryInTerminal()
    {
        if (_tree.SelectedNode?.Tag is string path)
        {
            string targetDir = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? _projectRoot ?? "");
            if (Directory.Exists(targetDir))
            {
                _terminalCwd = targetDir;
                AppendRunOutput($"\n[终端目录已切换至: {targetDir}]\n", Color.Yellow);
                _txtTerminalInput.Focus();
            }
        }
        else if (_projectRoot != null && Directory.Exists(_projectRoot))
        {
            _terminalCwd = _projectRoot;
            AppendRunOutput($"\n[终端目录已切换至: {_projectRoot}]\n", Color.Yellow);
            _txtTerminalInput.Focus();
        }
    }

    private void RunSelectedExecutable()
    {
        if (_tree.SelectedNode?.Tag is string path && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var workingDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(workingDir))
            {
                _terminalCwd = workingDir;
                AppendRunOutput($"\n[准备运行程序，终端目录已切换至: {workingDir}]\n", Color.Yellow);
            }
            
            // Generate a command to run it in the terminal
            string exeName = Path.GetFileName(path);
            _txtTerminalInput.Text = $".\\{exeName}";
            _txtTerminalInput.Focus();
            // Automatically simulate pressing enter to run it
            _ = RunTerminalCommandAsync(_txtTerminalInput.Text);
            _txtTerminalInput.Clear();
        }
    }

    private void CreateNewDirectory()
    {
        if (_projectRoot == null) return;

        string targetDir = _projectRoot;
        if (_tree.SelectedNode?.Tag is string path)
        {
            if (Directory.Exists(path))
            {
                targetDir = path;
            }
            else if (File.Exists(path))
            {
                targetDir = Path.GetDirectoryName(path) ?? _projectRoot;
            }
        }

        string input = Microsoft.VisualBasic.Interaction.InputBox("请输入新目录名称：", "新建目录", "NewFolder");
        if (string.IsNullOrWhiteSpace(input)) return;

        try
        {
            string newDirPath = Path.Combine(targetDir, input);
            if (Directory.Exists(newDirPath))
            {
                MessageBox.Show("该目录已存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (File.Exists(newDirPath))
            {
                MessageBox.Show("存在同名文件，无法创建目录！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Directory.CreateDirectory(newDirPath);
            Log($"✔ 已创建目录: {newDirPath}");
            
            // 刷新文件树
            RefreshProjectTree();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建目录失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 在当前选中目录或项目根目录下创建一个新的文本文件，并自动打开到编辑器中。
    /// </summary>
    private void CreateNewTextFile()
    {
        if (_projectRoot == null)
        {
            Warn("请先打开一个项目目录");
            return;
        }

        string targetDir = _projectRoot;
        if (_tree.SelectedNode?.Tag is string path)
        {
            if (Directory.Exists(path))
            {
                targetDir = path;
            }
            else if (File.Exists(path))
            {
                targetDir = Path.GetDirectoryName(path) ?? _projectRoot;
            }
        }

        string input = Microsoft.VisualBasic.Interaction.InputBox("请输入新文本文件名称：", "新建文本文件", "NewFile.txt");
        if (string.IsNullOrWhiteSpace(input)) return;

        var fileName = input.Trim();
        if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
        {
            fileName += ".txt";
        }

        try
        {
            string newFilePath = Path.Combine(targetDir, fileName);
            if (File.Exists(newFilePath) || Directory.Exists(newFilePath))
            {
                MessageBox.Show("同名文件或目录已存在！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            File.WriteAllText(newFilePath, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Log($"✔ 已创建文本文件: {newFilePath}");
            RefreshProjectTree();
            OpenFileInEditor(newFilePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建文本文件失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedNode()
    {
        if (_tree.SelectedNode?.Tag is string path && File.Exists(path))
        {
            var fileName = Path.GetFileName(path);
            var confirm = MessageBox.Show($"确定要删除文件 {fileName} 吗？\n文件将被移动到回收站目录。", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            
            if (confirm == DialogResult.Yes)
            {
                try
                {
                    // 关闭可能打开的同名编辑器标签
                    foreach (TabPage tab in _editorTabs.TabPages)
                    {
                        if (tab.Tag is string p && string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                        {
                            _editorTabs.TabPages.Remove(tab);
                            break;
                        }
                    }

                    // 计算回收站路径
                    string backupRoot = GetDeletedFilesBackupRoot();
                    
                    var now = DateTime.Now;
                    string dateDir = now.ToString("yyyy-MM-dd");
                    string targetDir = Path.Combine(backupRoot, dateDir);
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    string timeSuffix = now.ToString("HHmm");
                    string targetFileName = $"{fileName}.{timeSuffix}.bak";
                    string targetPath = Path.Combine(targetDir, targetFileName);

                    File.Move(path, targetPath);
                    Log($"✔ 文件已移至回收站: {targetPath}");
                    
                    // 刷新文件树，同时保留原先展开的目录状态
                    RefreshProjectTree();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    private static void AddDirectoryNodes(TreeNode parent, string dir, int depth)
    {
        if (depth > 6) return;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d))
            {
                var name = Path.GetFileName(sub);
                if (_skipDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                var node = new TreeNode("📁  " + name) { Tag = sub, ForeColor = FgText };
                parent.Nodes.Add(node);
                AddDirectoryNodes(node, sub, depth + 1);
            }
            foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f))
            {
                var name = Path.GetFileName(file);
                var node = new TreeNode("📄  " + name) { Tag = file, ForeColor = FgText };
                if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                {
                    node.ForeColor = FgMuted;
                }
                parent.Nodes.Add(node);
            }
        }
        catch { /* 忽略无权限目录 */ }
    }
}
