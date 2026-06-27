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
    private void BuildMenu()
    {
        var fileMenu = new ToolStripMenuItem("文件(&F)");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("新建文本文件(&N)...", null, (_, _) => CreateNewTextFile()) { ShortcutKeys = Keys.Control | Keys.N });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("新建项目目录(&P)...", null, (_, _) => CreateNewProjectDirectory()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.N });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("打开目录(&O)...", null, (_, _) => BtnOpen_Click()) { ShortcutKeys = Keys.Control | Keys.O });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("保存(&S)", null, (_, _) => SaveCurrentEditor()) { ShortcutKeys = Keys.Control | Keys.S });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("另存为(&A)...", null, (_, _) => SaveCurrentEditorAs()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S });
        fileMenu.DropDownItems.Add(_menuRecent);
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("刷新(&R)", null, (_, _) => RefreshProjectTree()) { ShortcutKeys = Keys.F5 });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("归档所有 .bak", null, (_, _) => ArchiveAllProjectBakFiles()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("设置(&T)...", null, (_, _) => ShowSettings()));
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("退出(&X)", null, (_, _) => Close()));

        var editMenu = new ToolStripMenuItem("编辑(&E)");
        editMenu.DropDownItems.Add(new ToolStripMenuItem("撤销(&U)", null, (_, _) => DoUndo()) { ShortcutKeys = Keys.Control | Keys.Z });

        var helpMenu = new ToolStripMenuItem("帮助(&H)");
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("关于", null, (_, _) => ShowAbout()));

        _menu.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, helpMenu });
        _menu.BackColor = BgHeader;
        _menu.ForeColor = FgText;
        _menu.Renderer = new DarkMenuRenderer();
        MainMenuStrip = _menu;
    }

    private void BuildToolbar()
    {
        var btnNewProject = MakeToolButton("🗂 新建项目", (_, _) => CreateNewProjectDirectory());
        var btnOpen = MakeToolButton("📂 打开目录", (_, _) => BtnOpen_Click());
        var btnSave = MakeToolButton("💾 保存", (_, _) => SaveCurrentEditor());
        var btnMyChrome = MakeToolButton("🌐 启动 MyChrome", async (_, _) => await LaunchMyChromeAsync());
        var btnNewSession = MakeToolButton("🆕 新 Session", (_, _) => StartNewPromptSession());
        var btnCloseEditor = MakeToolButton("✖ 关闭代码页", (_, _) => CloseCurrentEditorTab());
        var btnGen = MakeToolButton("✨ 生成提示词", BtnGen_Click);
        var btnCopy = MakeToolButton("📋 复制提示词", (_, _) => CopyPromptToClipboard());
        var btnPasteJson = MakeToolButton("📥 粘贴返回内容", (_, _) => HandleAiJsonDialogAction(ShowAiJsonInputDialog()));
        var btnPreview = MakeToolButton("🔍 预览 Diff", BtnPreview_Click);
        var btnApply = MakeToolButton("✅ 应用", BtnApply_Click);
        var btnUndo = MakeToolButton("↩ 撤销", (_, _) => DoUndo());
        var btnClearLog = MakeToolButton("🧹 清空日志", (_, _) => ClearLogPanel());

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            btnNewProject, btnOpen, btnSave, btnMyChrome, btnNewSession, btnCloseEditor, new ToolStripSeparator(),
            btnGen, btnCopy, btnPasteJson, new ToolStripSeparator(),
            btnPreview, btnApply, btnUndo, new ToolStripSeparator(),
            new ToolStripControlHost(_chkIncludeAll) { BackColor = BgPanel },
            new ToolStripControlHost(_chkBackup) { BackColor = BgPanel },
            new ToolStripSeparator(),
            btnClearLog,
        });
        _toolbar.BackColor = BgPanel;
        _toolbar.ForeColor = FgText;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
    }

    private ToolStripButton MakeToolButton(string text, EventHandler onClick)
    {
        var btn = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, Font = new Font("Segoe UI", 9) };
        btn.Click += onClick;
        return btn;
    }

    private void BuildLayout()
    {
        // 左侧：文件树 + 我的计划
        var leftSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = (int)(Height * 0.5),
            BackColor = BgHeader,
            SplitterWidth = 4
        };

        var leftTopPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BgPanel };
        leftTopPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        leftTopPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var treeHeaderPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = BgHeader };
        treeHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        treeHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        treeHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        treeHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        treeHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        _lblTree.Text = "  📁  项目文件";
        _lblTree.Dock = DockStyle.Fill;
        _lblTree.TextAlign = ContentAlignment.MiddleLeft;
        _lblTree.BackColor = BgHeader;
        _lblTree.ForeColor = FgText;
        _lblTree.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        Button createTreeActionButton(string text, string toolTip, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = BgHeader,
                ForeColor = FgText,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += onClick;
            var tip = new ToolTip();
            tip.SetToolTip(button, toolTip);
            return button;
        }
        var btnNewFileTree = createTreeActionButton("+F", "新建文本文件", (_, _) => CreateNewTextFile());
        var btnNewFolderTree = createTreeActionButton("+D", "新建目录", (_, _) => CreateNewDirectory());
        var btnRefreshTree = new Button
        {
            Text = "↻",
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            BackColor = BgHeader,
            ForeColor = FgText,
            Cursor = Cursors.Hand
        };
        btnRefreshTree.FlatAppearance.BorderSize = 0;
        btnRefreshTree.Click += (_, _) => RefreshProjectTree();
        var btnArchiveBakTree = createTreeActionButton("🗃", "归档所有 .bak 文件", (_, _) => ArchiveAllProjectBakFiles());
        _tree.Dock = DockStyle.Fill;
        _tree.BackColor = BgPanel;
        _tree.ForeColor = FgText;
        _tree.Font = new Font("Cascadia Mono", 9);
        _tree.BorderStyle = BorderStyle.None;
        _tree.ItemHeight = 22;
        _tree.CheckBoxes = true;
        _tree.AfterSelect += Tree_AfterSelect;
        _tree.NodeMouseDoubleClick += Tree_NodeMouseDoubleClick;
        _tree.AfterCheck += Tree_AfterCheck;
        _tree.MouseUp += Tree_MouseUp;
        _tree.ContextMenuStrip = _treeMenu;
        
        // 文件树右键菜单
        var runExeMenuItem = new ToolStripMenuItem("运行该程序", null, (_, _) => RunSelectedExecutable());
        _treeMenu.Items.Add(runExeMenuItem);
        _treeMenu.Items.Add(new ToolStripSeparator());
        _treeMenu.Items.Add("新建文本文件", null, (_, _) => CreateNewTextFile());
        _treeMenu.Items.Add("新建目录", null, (_, _) => CreateNewDirectory());
        _treeMenu.Items.Add("刷新", null, (_, _) => RefreshProjectTree());
        _treeMenu.Items.Add("归档所有 .bak", null, (_, _) => ArchiveAllProjectBakFiles());
        _treeMenu.Items.Add("打开当前目录", null, (_, _) => OpenDirectoryInExplorer());
        _treeMenu.Items.Add("在集成终端打开", null, (_, _) => OpenDirectoryInTerminal());
        _treeMenu.Items.Add(new ToolStripSeparator());
        _treeMenu.Items.Add("重命名", null, (_, _) => RenameSelectedNode());
        _treeMenu.Items.Add("删除", null, (_, _) => DeleteSelectedNode());
        _treeMenu.Opening += (_, e) => 
        {
            // 只要是在树上右击就允许打开，因为“新建目录”可以在根目录创建
            bool isExe = _tree.SelectedNode?.Tag is string path && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
            runExeMenuItem.Visible = isExe;
        };

        treeHeaderPanel.Controls.Add(_lblTree, 0, 0);
        treeHeaderPanel.Controls.Add(btnNewFileTree, 1, 0);
        treeHeaderPanel.Controls.Add(btnNewFolderTree, 2, 0);
        treeHeaderPanel.Controls.Add(btnRefreshTree, 3, 0);
        treeHeaderPanel.Controls.Add(btnArchiveBakTree, 4, 0);
        leftTopPanel.Controls.Add(treeHeaderPanel, 0, 0);
        leftTopPanel.Controls.Add(_tree, 0, 1);
        leftSplit.Panel1.Controls.Add(leftTopPanel);

        // 左下：我的计划 + AI 返回内容按钮
        var planPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        planPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        planPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        planPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var aiButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BgPanel,
            Padding = new Padding(0, 4, 0, 0)
        };

        var btnPasteAiJson = new Button
        {
            Text = "📥 粘贴/编辑返回内容",
            Width = 140,
            Height = 30,
            Margin = new Padding(0, 0, 8, 0),
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnPasteAiJson.FlatAppearance.BorderSize = 0;
        btnPasteAiJson.Click += (_, _) => HandleAiJsonDialogAction(ShowAiJsonInputDialog());

        var btnPreviewAiJson = new Button
        {
            Text = "🔍 预览返回内容",
            Width = 110,
            Height = 30,
            Margin = new Padding(0, 0, 8, 0),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnPreviewAiJson.FlatAppearance.BorderSize = 0;
        btnPreviewAiJson.Click += BtnPreview_Click;

        var btnQuoteContext = new Button
        {
            Text = "📎 引用代码和计划",
            Width = 140,
            Height = 30,
            Margin = new Padding(0, 0, 0, 0),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnQuoteContext.FlatAppearance.BorderSize = 0;
        btnQuoteContext.Click += BtnQuoteContext_Click;

        aiButtonPanel.Controls.Add(btnPasteAiJson);
        aiButtonPanel.Controls.Add(btnPreviewAiJson);

        planPanel.Controls.Add(aiButtonPanel, 0, 0);

        _txtPlan.Dock = DockStyle.Fill;
        _txtPlan.BorderStyle = BorderStyle.FixedSingle;
        planPanel.Controls.Add(_txtPlan, 0, 1);

        var bottomPlanButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = BgPanel,
            Padding = new Padding(0, 4, 0, 0)
        };

        var btnSendPlan = new Button
        {
            Text = "✨ 生成并复制提示词",
            Width = 160,
            Height = 32,
            Margin = new Padding(8, 0, 0, 0),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnSendPlan.FlatAppearance.BorderSize = 0;
        btnSendPlan.Click += (_, _) => 
        {
            BtnGen_Click(this, EventArgs.Empty);
            CopyPromptToClipboard();
        };

        bottomPlanButtonPanel.Controls.Add(btnSendPlan);
        bottomPlanButtonPanel.Controls.Add(btnQuoteContext);

        planPanel.Controls.Add(bottomPlanButtonPanel, 0, 2);
        
        leftSplit.Panel2.Controls.Add(planPanel);

        var pnlOutputRun = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BgDark
        };
        pnlOutputRun.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        pnlOutputRun.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Button createRunPanelButton(string text, Color backColor, EventHandler onClick, int width)
        {
            var button = new Button
            {
                Text = text,
                Width = width,
                Height = 32,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 8, 0),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += onClick;
            return button;
        }

        var runToolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BgPanel,
            Padding = new Padding(10, 8, 10, 8)
        };
        runToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        runToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var runInfoPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = BgPanel,
            Margin = new Padding(0)
        };
        var runHintLabel = new Label
        {
            Text = "▶ 命令输出区：单独执行编译或运行，并把结果一键回传给 AI。",
            AutoSize = true,
            ForeColor = FgMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 8, 0, 0),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        runInfoPanel.Controls.Add(runHintLabel);
        runInfoPanel.Controls.Add(_lblRunSessionState);
        var runButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BgPanel,
            AutoSize = true
        };

        var btnBuildOutput = createRunPanelButton("🛠 编译", Accent, async (_, _) => await BuildCppCodeAsync(), 92);
        var btnRunOutput = createRunPanelButton("▶ 运行", Color.FromArgb(0, 153, 102), async (_, _) => await RunBuiltProgramAsync(), 92);
        var btnCopyOutput = createRunPanelButton("📋 复制给 AI", Color.FromArgb(60, 60, 60), BtnCopyOutput_Click, 116);
        _btnStopRunOutput.Margin = new Padding(0, 0, 8, 0);
        _btnStopRunOutput.Cursor = Cursors.Hand;
        _btnStopRunOutput.FlatAppearance.BorderSize = 0;
        _btnClearRunOutput.Margin = new Padding(0, 0, 8, 0);
        _btnClearRunOutput.Cursor = Cursors.Hand;
        _btnClearRunOutput.FlatAppearance.BorderSize = 0;

        runButtonPanel.Controls.Add(btnBuildOutput);
        runButtonPanel.Controls.Add(btnRunOutput);
        runButtonPanel.Controls.Add(_btnStopRunOutput);
        runButtonPanel.Controls.Add(btnCopyOutput);
        runButtonPanel.Controls.Add(_btnClearRunOutput);
        runButtonPanel.Controls.Add(_chkEnableCommandTimeout);
        runButtonPanel.Controls.Add(_chkAutoScrollOutput);
        runButtonPanel.Controls.Add(_chkAutoScrollLog);
        runToolbar.Controls.Add(runInfoPanel, 0, 0);
        runToolbar.Controls.Add(runButtonPanel, 1, 0);

        var runOutputPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BgDark,
            Padding = new Padding(1, 8, 1, 1),
            RowCount = 2,
            ColumnCount = 1
        };
        runOutputPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        runOutputPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        runOutputPanel.Controls.Add(_txtRunOutput, 0, 0);
        runOutputPanel.Controls.Add(_txtTerminalInput, 0, 1);
        
        _txtTerminalInput.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await RunTerminalCommandAsync(_txtTerminalInput.Text);
                _txtTerminalInput.Clear();
            }
        };

        _bottomOutputTabs.Appearance = TabAppearance.Normal;
        _bottomOutputTabs.Dock = DockStyle.Fill;
        _bottomOutputTabs.Controls.Clear();
        _tabRunOutput.Controls.Clear();
        _tabOperationLog.Controls.Clear();
        _tabRunOutput.Controls.Add(runOutputPanel);
        _txtLog.Dock = DockStyle.Fill;
        _tabOperationLog.Controls.Add(_txtLog);
        if (_bottomOutputTabs.TabPages.Count == 0)
        {
            _bottomOutputTabs.TabPages.Add(_tabRunOutput);
            _bottomOutputTabs.TabPages.Add(_tabOperationLog);
        }

        pnlOutputRun.Controls.Add(runToolbar, 0, 0);
        pnlOutputRun.Controls.Add(_bottomOutputTabs, 0, 1);

        // 中间上半区：代码查看
        var centerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = (int)(Height * 0.65),
            BackColor = BgHeader,
            SplitterWidth = 4
        };

        var editorPanel = BuildEditorPanel();
        centerSplit.Panel1.Controls.Add(editorPanel);
        centerSplit.Panel2.Controls.Add(pnlOutputRun);

        // 右侧：计划历史 + AI 返回历史 + AI 命令收藏
        _rightPanel.RowStyles.Clear();
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        _rightPanel.Controls.Clear();
        _lblSavedPlanHeader.Text = "  ▼  计划历史";
        _savedPlanPanel.RowStyles.Clear();
        _savedPlanPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _savedPlanPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        _savedPlanPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        _savedPlanPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        _savedPlanPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _savedPlanPanel.Controls.Clear();
        _txtSavedPlanSearch.Dock = DockStyle.Fill;
        _txtSavedPlanSearch.PlaceholderText = "搜索计划历史...";
        _savedPlanPanel.Controls.Add(_txtSavedPlanSearch, 0, 0);
        _savedPlanPanel.Controls.Add(_lblSavedPlansSummary, 0, 1);
        _lstSavedPlans.Dock = DockStyle.Fill;
        _savedPlanPanel.Controls.Add(_lstSavedPlans, 0, 2);
        _txtSavedPlanDetail.Dock = DockStyle.Fill;
        _savedPlanPanel.Controls.Add(_txtSavedPlanDetail, 0, 3);
        var savedPlanButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        savedPlanButtonPanel.Controls.Add(_btnUseSavedPlan);
        savedPlanButtonPanel.Controls.Add(_btnCopySavedPlan);
        savedPlanButtonPanel.Controls.Add(_btnPinSavedPlan);
        savedPlanButtonPanel.Controls.Add(_btnDeleteSavedPlan);
        _savedPlanPanel.Controls.Add(savedPlanButtonPanel, 0, 4);
        _lblSavedJsonHeader.Text = "  ▼  AI 返回历史";
        _savedJsonPanel.RowStyles.Clear();
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _savedJsonPanel.Controls.Clear();
        _txtSavedJsonSearch.Dock = DockStyle.Fill;
        _txtSavedJsonSearch.PlaceholderText = "搜索 AI 返回内容...";
        _savedJsonPanel.Controls.Add(_txtSavedJsonSearch, 0, 0);
        _savedJsonPanel.Controls.Add(_lblSavedJsonSummary, 0, 1);
        _lstSavedJson.Dock = DockStyle.Fill;
        _savedJsonPanel.Controls.Add(_lstSavedJson, 0, 2);
        _txtSavedJsonDetail.Dock = DockStyle.Fill;
        _savedJsonPanel.Controls.Add(_txtSavedJsonDetail, 0, 3);
        var savedJsonButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        savedJsonButtonPanel.Controls.Add(_btnUseSavedJson);
        savedJsonButtonPanel.Controls.Add(_btnCopySavedJson);
        savedJsonButtonPanel.Controls.Add(_btnPinSavedJson);
        savedJsonButtonPanel.Controls.Add(_btnDeleteSavedJson);
        _savedJsonPanel.Controls.Add(savedJsonButtonPanel, 0, 4);
        _lblSavedCommandsHeader.Text = "  ▼  AI 命令收藏";
        _savedCommandsPanel.RowStyles.Clear();
        _savedCommandsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _savedCommandsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        _savedCommandsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        _savedCommandsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        _savedCommandsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        _savedCommandsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _savedCommandsPanel.Controls.Clear();
        _txtSavedCommandSearch.Dock = DockStyle.Fill;
        _txtSavedCommandSearch.PlaceholderText = "搜索收藏命令...";
        _savedCommandsPanel.Controls.Add(_txtSavedCommandSearch, 0, 0);
        _savedCommandsPanel.Controls.Add(_lblSavedCommandsSummary, 0, 1);
        _lstSavedCommands.Dock = DockStyle.Fill;
        _savedCommandsPanel.Controls.Add(_lstSavedCommands, 0, 2);
        var lblSavedDetail = new Label
        {
            Text = "  命令详情",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = FgMuted,
            BackColor = BgPanel,
            Font = new Font("Segoe UI", 8, FontStyle.Bold)
        };
        _savedCommandsPanel.Controls.Add(lblSavedDetail, 0, 3);
        _txtSavedCommandDetail.Dock = DockStyle.Fill;
        _savedCommandsPanel.Controls.Add(_txtSavedCommandDetail, 0, 4);
        var savedButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        savedButtonPanel.Controls.Add(_btnRunSavedCommand);
        savedButtonPanel.Controls.Add(_btnEditSavedCommand);
        savedButtonPanel.Controls.Add(_btnCopySavedCommand);
        savedButtonPanel.Controls.Add(_btnMoveSavedCommandUp);
        savedButtonPanel.Controls.Add(_btnMoveSavedCommandDown);
        savedButtonPanel.Controls.Add(_btnSetBuildCommand);
        savedButtonPanel.Controls.Add(_btnSetRunCommand);
        savedButtonPanel.Controls.Add(_btnDeleteSavedCommand);
        _savedCommandsPanel.Controls.Add(savedButtonPanel, 0, 5);
        _rightPanel.Controls.Add(_lblSavedPlanHeader, 0, 0);
        _rightPanel.Controls.Add(_savedPlanPanel, 0, 1);
        _rightPanel.Controls.Add(_lblSavedJsonHeader, 0, 2);
        _rightPanel.Controls.Add(_savedJsonPanel, 0, 3);
        _rightPanel.Controls.Add(_lblSavedCommandsHeader, 0, 4);
        _rightPanel.Controls.Add(_savedCommandsPanel, 0, 5);

        _centerRightSplit.Panel1.Controls.Clear();
        _centerRightSplit.Panel2.Controls.Clear();
        _centerRightSplit.Panel1.Controls.Add(centerSplit);
        _centerRightSplit.Panel2.Controls.Add(_rightPanel);

        _leftCenterSplit.Panel1.Controls.Clear();
        _leftCenterSplit.Panel2.Controls.Clear();
        _leftCenterSplit.Panel1.Controls.Add(leftSplit);
        _leftCenterSplit.Panel2.Controls.Add(_centerRightSplit);

        _mainContent = _leftCenterSplit;
    }

    /// <summary>
    /// 初始化右侧计划历史的右键菜单。
    /// </summary>
    private void InitializeSavedPlanContextMenu()
    {
        _savedPlanMenu.ShowImageMargin = false;
        _savedPlanMenu.BackColor = BgPanel;
        _savedPlanMenu.ForeColor = FgText;
        _savedPlanMenu.Items.Add("回填到计划区", null, (_, _) => UseSelectedSavedPlan());
        _savedPlanMenu.Items.Add("复制计划", null, (_, _) => CopySelectedSavedPlan());
        _savedPlanMenu.Items.Add(new ToolStripSeparator());
        _savedPlanMenu.Items.Add("置顶 / 取消置顶", null, (_, _) => ToggleSelectedSavedPlanPinned());
        _savedPlanMenu.Items.Add("删除", null, (_, _) => DeleteSelectedSavedPlan());
        _lstSavedPlans.ContextMenuStrip = _savedPlanMenu;
    }

    /// <summary>
    /// 初始化右侧 AI 返回历史的右键菜单。
    /// </summary>
    private void InitializeSavedJsonContextMenu()
    {
        _savedJsonMenu.ShowImageMargin = false;
        _savedJsonMenu.BackColor = BgPanel;
        _savedJsonMenu.ForeColor = FgText;
        _savedJsonMenu.Items.Add("回填到编辑区", null, (_, _) => UseSelectedSavedJson());
        _savedJsonMenu.Items.Add("回填并预览", null, (_, _) => PreviewSelectedSavedJson());
        _savedJsonMenu.Items.Add("复制 JSON", null, (_, _) => CopySelectedSavedJson());
        _savedJsonMenu.Items.Add(new ToolStripSeparator());
        _savedJsonMenu.Items.Add("置顶 / 取消置顶", null, (_, _) => ToggleSelectedSavedJsonPinned());
        _savedJsonMenu.Items.Add("删除", null, (_, _) => DeleteSelectedSavedJson());
        _lstSavedJson.ContextMenuStrip = _savedJsonMenu;
    }

    /// <summary>
    /// 应用右侧三个区域的折叠状态。
    /// </summary>
    private void ApplyRightPanelLayout()
    {
        _savedPlanPanel.Visible = !_isSavedPlanCollapsed;
        _savedJsonPanel.Visible = !_isSavedJsonCollapsed;
        _savedCommandsPanel.Visible = !_isSavedCommandsCollapsed;

        _rightPanel.RowStyles[1].SizeType = _isSavedPlanCollapsed ? SizeType.Absolute : SizeType.Percent;
        _rightPanel.RowStyles[1].Height = _isSavedPlanCollapsed ? 0 : 34;
        _rightPanel.RowStyles[3].SizeType = _isSavedJsonCollapsed ? SizeType.Absolute : SizeType.Percent;
        _rightPanel.RowStyles[3].Height = _isSavedJsonCollapsed ? 0 : 33;
        _rightPanel.RowStyles[5].SizeType = _isSavedCommandsCollapsed ? SizeType.Absolute : SizeType.Percent;
        _rightPanel.RowStyles[5].Height = _isSavedCommandsCollapsed ? 0 : 33;

        UpdateRightPanelHeaders();
    }

    /// <summary>
    /// 切换右侧某个区域的折叠状态。
    /// </summary>
    private void ToggleRightPanelSection(RightPanelSection section)
    {
        switch (section)
        {
            case RightPanelSection.SavedPlan:
                _isSavedPlanCollapsed = !_isSavedPlanCollapsed;
                break;
            case RightPanelSection.SavedJson:
                _isSavedJsonCollapsed = !_isSavedJsonCollapsed;
                break;
            case RightPanelSection.SavedCommands:
                _isSavedCommandsCollapsed = !_isSavedCommandsCollapsed;
                break;
        }

        PersistRightPanelCollapseState();
        ApplyRightPanelLayout();
    }

    /// <summary>
    /// 根据当前列表和日志状态刷新右侧标题中的数量提示。
    /// </summary>
    private void UpdateRightPanelHeaders()
    {
        var allPlanCount = string.IsNullOrWhiteSpace(_projectRoot) ? 0 : _settings.GetSavedPlanHistory(_projectRoot).Count;
        var allJsonCount = string.IsNullOrWhiteSpace(_projectRoot) ? 0 : _settings.GetSavedJsonHistory(_projectRoot).Count;
        var allCommandCount = string.IsNullOrWhiteSpace(_projectRoot) ? 0 : _settings.GetSavedCommands(_projectRoot).Count;
        var logCount = GetLogEntryCount();
        var planBadge = _currentSavedPlans.Count == allPlanCount
            ? $"[{allPlanCount}]"
            : $"[{_currentSavedPlans.Count}/{allPlanCount}]";
        var jsonBadge = _currentSavedJsonHistory.Count == allJsonCount
            ? $"[{allJsonCount}]"
            : $"[{_currentSavedJsonHistory.Count}/{allJsonCount}]";
        var commandBadge = _currentSavedCommands.Count == allCommandCount
            ? $"[{allCommandCount}]"
            : $"[{_currentSavedCommands.Count}/{allCommandCount}]";

        _lblSavedPlanHeader.Text = $"  {(_isSavedPlanCollapsed ? "▶" : "▼")}  计划历史 {planBadge}";
        _lblSavedJsonHeader.Text = $"  {(_isSavedJsonCollapsed ? "▶" : "▼")}  AI 返回历史 {jsonBadge}";
        _lblSavedCommandsHeader.Text = $"  {(_isSavedCommandsCollapsed ? "▶" : "▼")}  AI 命令收藏 {commandBadge}";
        _tabOperationLog.Text = $"操作日志 [{logCount}]";
    }

    /// <summary>
    /// 保存右侧面板折叠状态，确保下次启动时恢复相同布局。
    /// </summary>
    private void PersistRightPanelCollapseState()
    {
        _settings.IsSavedPlanCollapsed = _isSavedPlanCollapsed;
        _settings.IsSavedJsonCollapsed = _isSavedJsonCollapsed;
        _settings.IsSavedCommandsCollapsed = _isSavedCommandsCollapsed;
        _settings.Save();
    }

    /// <summary>
    /// 应用左右面板宽度设置，让三栏布局支持拖拽后保持上次尺寸。
    /// </summary>
    private void ApplySplitterSettings()
    {
        _leftCenterSplit.SplitterMoved -= PanelSplitter_SplitterMoved;
        _centerRightSplit.SplitterMoved -= PanelSplitter_SplitterMoved;
        _suppressPanelSplitterSave = true;
        try
        {
            ConfigureSplitterMinimums();
            ApplyLeftPanelWidth(_settings.LeftPanelWidth);
            ApplyRightPanelWidth(_settings.RightPanelWidth);
        }
        finally
        {
            _suppressPanelSplitterSave = false;
            _leftCenterSplit.SplitterMoved += PanelSplitter_SplitterMoved;
            _centerRightSplit.SplitterMoved += PanelSplitter_SplitterMoved;
        }
    }

    /// <summary>
    /// 在窗体尺寸稳定后设置分栏最小尺寸，避免初始化阶段触发非法 SplitterDistance。
    /// </summary>
    private void ConfigureSplitterMinimums()
    {
        _leftCenterSplit.Panel1MinSize = 220;
        _leftCenterSplit.Panel2MinSize = 560;
        _centerRightSplit.Panel1MinSize = 420;
        _centerRightSplit.Panel2MinSize = 320;
    }

    /// <summary>
    /// 应用左侧文件树面板宽度。
    /// </summary>
    private void ApplyLeftPanelWidth(int leftWidth)
    {
        if (_leftCenterSplit.Width <= 0) return;

        var targetWidth = leftWidth > 0 ? leftWidth : 280;
        var maxWidth = Math.Max(_leftCenterSplit.Panel1MinSize, _leftCenterSplit.Width - _leftCenterSplit.Panel2MinSize - _leftCenterSplit.SplitterWidth);
        var appliedWidth = Math.Min(Math.Max(targetWidth, _leftCenterSplit.Panel1MinSize), maxWidth);
        _leftCenterSplit.SplitterDistance = appliedWidth;
    }

    /// <summary>
    /// 应用右侧侧栏宽度。
    /// </summary>
    private void ApplyRightPanelWidth(int rightWidth)
    {
        if (_centerRightSplit.Width <= 0) return;

        var targetWidth = rightWidth > 0 ? rightWidth : 520;
        var minDistance = _centerRightSplit.Panel1MinSize;
        var maxDistance = Math.Max(minDistance, _centerRightSplit.Width - _centerRightSplit.Panel2MinSize - _centerRightSplit.SplitterWidth);
        var preferredDistance = _centerRightSplit.Width - targetWidth - _centerRightSplit.SplitterWidth;
        var appliedDistance = Math.Min(Math.Max(preferredDistance, minDistance), maxDistance);
        _centerRightSplit.SplitterDistance = appliedDistance;
    }

    /// <summary>
    /// 保存左右拖拽后的面板尺寸，便于下次启动恢复。
    /// </summary>
    private void SavePanelSplitterSettings()
    {
        if (_suppressPanelSplitterSave) return;

        if (_leftCenterSplit.Width > 0)
        {
            _settings.LeftPanelWidth = _leftCenterSplit.SplitterDistance;
        }

        if (_centerRightSplit.Width > 0)
        {
            _settings.RightPanelWidth = _centerRightSplit.Width - _centerRightSplit.SplitterDistance - _centerRightSplit.SplitterWidth;
        }

        _settings.Save();
    }

    /// <summary>
    /// 主界面分栏被用户拖动后保存当前布局宽度。
    /// </summary>
    private void PanelSplitter_SplitterMoved(object? sender, SplitterEventArgs e)
    {
        SavePanelSplitterSettings();
    }

    /// <summary>
    /// 应用 AI 返回内容文本框的自动换行设置。
    /// </summary>
    private void ApplyAiWrapSetting()
    {
        _txtAi.WordWrap = _chkAiWrap.Checked;
        _txtAi.ScrollBars = _chkAiWrap.Checked ? ScrollBars.Vertical : ScrollBars.Both;
    }
}
