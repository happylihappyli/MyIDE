using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyIDE.Forms;
using MyIDE.Models;
using MyIDE.Services;

namespace MyIDE;

/// <summary>
/// 主窗体（v2）：暗色主题、标签页布局、差异预览、撤销栈
/// </summary>
public class MainForm : Form
{
    // 暗色主题色板
    private static readonly Color BgDark = Color.FromArgb(30, 30, 30);
    private static readonly Color BgPanel = Color.FromArgb(45, 45, 48);
    private static readonly Color BgHeader = Color.FromArgb(37, 37, 38);
    private static readonly Color FgText = Color.FromArgb(212, 212, 212);
    private static readonly Color FgMuted = Color.FromArgb(150, 150, 150);
    private static readonly Color Accent = Color.FromArgb(0, 122, 204);
    private static readonly Color Success = Color.FromArgb(78, 201, 176);
    private static readonly Color Warning = Color.FromArgb(220, 220, 170);
    private static readonly Color Error = Color.FromArgb(244, 135, 113);
    private static readonly HttpClient AiBrowserHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private string? _projectRoot;
    private string _lastGeneratedPrompt = "";
    
    // C++ 运行输出相关的控件
    private TabPage _tabRunOutput = new TabPage("▶ 运行输出");
    private RichTextBox _txtRunOutput = new RichTextBox
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.FromArgb(200, 200, 200),
        Font = new Font("Cascadia Mono", 10),
        ReadOnly = true,
        BorderStyle = BorderStyle.None
    };
    private readonly UndoStack _undo = new();
    private readonly AppSettings _settings = AppSettings.Load();

    // 顶部：菜单
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _menuRecent = new("最近打开(&R)");

    // 顶部：工具栏（大按钮）
    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, ImageScalingSize = new Size(16, 16) };

    // 主内容容器
    private Control? _mainContent;

    // 左侧：文件树
    private readonly TreeView _tree = new();
    private readonly Label _lblTree = new();

    // 中间：代码查看 + 工作流标签页
    private readonly Label _lblEditor = new();
    private readonly TabControl _editorTabs = new();
    private readonly TabControl _tabs = new() { Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(120, 28) };
    private readonly TextBox _txtPlan = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Cascadia Mono", 10), BorderStyle = BorderStyle.None, BackColor = BgDark, ForeColor = FgText };
    private readonly TextBox _txtAi = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = new Font("Cascadia Mono", 10), BorderStyle = BorderStyle.None, BackColor = BgDark, ForeColor = FgText };

    // 右侧：日志
    private readonly ListBox _lstSavedJson = new()
    {
        BackColor = BgDark,
        ForeColor = FgText,
        BorderStyle = BorderStyle.None,
        Font = new Font("Cascadia Mono", 9),
        HorizontalScrollbar = true
    };
    private readonly Label _lblSavedJsonSummary = new()
    {
        Text = "  暂无 AI JSON",
        ForeColor = FgMuted,
        BackColor = BgPanel,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 8, FontStyle.Bold)
    };
    private readonly TextBox _txtSavedJsonSearch = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = BgDark,
        ForeColor = FgText,
        Font = new Font("Segoe UI", 9)
    };
    private readonly TextBox _txtSavedJsonDetail = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = BgDark,
        ForeColor = FgText,
        BorderStyle = BorderStyle.None,
        Font = new Font("Cascadia Mono", 9)
    };
    private readonly Button _btnUseSavedJson = new()
    {
        Text = "回填",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(0, 122, 204),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnCopySavedJson = new()
    {
        Text = "复制",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnDeleteSavedJson = new()
    {
        Text = "删除",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(90, 45, 45),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnPinSavedJson = new()
    {
        Text = "置顶",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly ListBox _lstSavedCommands = new()
    {
        BackColor = BgDark,
        ForeColor = FgText,
        BorderStyle = BorderStyle.None,
        Font = new Font("Cascadia Mono", 9),
        HorizontalScrollbar = true
    };
    private readonly TextBox _txtSavedCommandSearch = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = BgDark,
        ForeColor = FgText,
        Font = new Font("Segoe UI", 9)
    };
    private readonly Label _lblSavedCommandsSummary = new()
    {
        Text = "  暂无收藏命令",
        ForeColor = FgMuted,
        BackColor = BgPanel,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 8, FontStyle.Bold)
    };
    private readonly TextBox _txtSavedCommandDetail = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = BgDark,
        ForeColor = FgText,
        BorderStyle = BorderStyle.None,
        Font = new Font("Cascadia Mono", 9)
    };
    private readonly Button _btnRunSavedCommand = new()
    {
        Text = "执行",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(0, 122, 204),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnSetBuildCommand = new()
    {
        Text = "设为编译",
        Width = 82,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnSetRunCommand = new()
    {
        Text = "设为运行",
        Width = 82,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnDeleteSavedCommand = new()
    {
        Text = "删除",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(90, 45, 45),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnMoveSavedCommandUp = new()
    {
        Text = "上移",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnMoveSavedCommandDown = new()
    {
        Text = "下移",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnCopySavedCommand = new()
    {
        Text = "复制",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly ContextMenuStrip _savedJsonMenu = new();
    private readonly ContextMenuStrip _savedCommandMenu = new();
    private readonly TextBox _txtLog = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = BgDark, ForeColor = Success, Font = new Font("Cascadia Mono", 9), BorderStyle = BorderStyle.None };
    private readonly TableLayoutPanel _rightPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = BgPanel };
    private readonly TableLayoutPanel _savedJsonPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = BgPanel, Padding = new Padding(6) };
    private readonly TableLayoutPanel _savedCommandsPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = BgPanel, Padding = new Padding(6) };
    private readonly Label _lblSavedJsonHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private readonly Label _lblSavedCommandsHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private readonly Label _lblLogHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private List<SavedAiJson> _currentSavedJsonHistory = new();
    private List<SavedAiCommand> _currentSavedCommands = new();
    private bool _isSavedJsonCollapsed;
    private bool _isSavedCommandsCollapsed;
    private bool _isLogCollapsed;

    // 选项
    private readonly CheckBox _chkIncludeAll = new() { Text = "提示词包含全部文件", Checked = false, ForeColor = FgText, FlatStyle = FlatStyle.Flat, BackColor = BgPanel };
    private readonly CheckBox _chkBackup = new() { Text = "修改前自动备份", Checked = true, ForeColor = FgText, FlatStyle = FlatStyle.Flat, BackColor = BgPanel };
    private readonly CheckBox _chkAiWrap = new() { Text = "JSON 自动换行", Checked = true, ForeColor = FgText, FlatStyle = FlatStyle.Flat, BackColor = BgPanel, AutoSize = true };

    // 状态栏
    private readonly StatusStrip _status = new() { BackColor = BgHeader, ForeColor = FgText, SizingGrip = false };
    private readonly ToolStripStatusLabel _lblStatus = new() { Text = "● 就绪", ForeColor = Success, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    private readonly ToolStripStatusLabel _lblProject = new() { Text = "未打开项目", ForeColor = FgMuted };
    private readonly ToolStripStatusLabel _lblAiBrowser = new() { Text = "AI 浏览器: 未发送", ForeColor = FgMuted };
    private readonly ToolStripStatusLabel _lblUndo = new() { Text = "可撤销: 0", ForeColor = FgMuted };
    private readonly ToolStripStatusLabel _lblCursor = new() { Text = "行: 1, 列: 1", ForeColor = FgMuted };

    /// <summary>
    /// 发送到 AI 浏览器桥接服务的请求结构。
    /// </summary>
    private sealed class AiBrowserSendRequest
    {
        public string Text { get; set; } = "";
        public bool AutoSend { get; set; } = true;
        public string Source { get; set; } = "myide";
    }

    /// <summary>
    /// AI 浏览器桥接服务返回的发送结果。
    /// </summary>
    private sealed class AiBrowserSendResult
    {
        public bool Ok { get; set; }
        public bool Sent { get; set; }
        public string Reason { get; set; } = "";
        public string Url { get; set; } = "";
        public string ReadBackText { get; set; } = "";
        public bool ReadBackMatched { get; set; }
        public string ComposerTag { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 生成适合日志和状态栏展示的短文本预览。
    /// </summary>
    private static string BuildShortPreview(string? text, int maxLength = 36)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var compact = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (compact.Length <= maxLength) return compact;
        return compact[..maxLength] + "...";
    }

    /// <summary>
    /// 把桥接服务返回的原因码转换成更清楚的中文描述。
    /// </summary>
    private static string DescribeAiBrowserResult(AiBrowserSendResult? result)
    {
        if (result == null) return "未收到浏览器回执";

        if (!string.IsNullOrWhiteSpace(result.Message))
            return result.Message;

        return result.Reason switch
        {
            "sent" => "已自动发送",
            "filled" => "已填充输入框",
            "browser_not_ready" => "浏览器未就绪",
            "page_not_supported" => "当前页面不支持发送",
            "input_not_found" => "未找到输入框",
            "input_set_failed" => "写入输入框失败",
            "send_button_not_found" => "未找到发送按钮",
            "script_result_empty" => "页面脚本无返回",
            "client_exception" => "调用桥接服务失败",
            _ => string.IsNullOrWhiteSpace(result.Reason) ? "未知状态" : result.Reason
        };
    }

    /// <summary>
    /// 根据桥接回执刷新状态栏和日志，让发送成功、失败和读回情况一眼可见。
    /// </summary>
    private void UpdateAiBrowserSendStatus(AiBrowserSendResult? result, string originalText)
    {
        if (result == null)
        {
            _lblAiBrowser.Text = "AI 浏览器: 未收到回执";
            _lblAiBrowser.ForeColor = Warning;
            Log("· AI 浏览器未返回可识别的结果。");
            return;
        }

        var desc = DescribeAiBrowserResult(result);
        var preview = BuildShortPreview(result.ReadBackText);

        if (result.Ok)
        {
            if (!string.IsNullOrWhiteSpace(result.ReadBackText))
            {
                _lblAiBrowser.Text = result.ReadBackMatched
                    ? $"AI 浏览器: {desc}，读回一致"
                    : $"AI 浏览器: {desc}，读回不一致";
                _lblAiBrowser.ForeColor = result.ReadBackMatched ? Success : Warning;
                Log(result.ReadBackMatched
                    ? $"✔ AI 浏览器状态：{desc}，页面读回一致。"
                    : $"⚠ AI 浏览器状态：{desc}，但页面读回内容与原文不一致。");
                if (!result.ReadBackMatched)
                {
                    Log($"· 原始发送内容：{BuildShortPreview(originalText)}");
                }
                Log($"· 页面读回内容：{preview}");
            }
            else
            {
                _lblAiBrowser.Text = $"AI 浏览器: {desc}";
                _lblAiBrowser.ForeColor = Success;
                Log($"✔ AI 浏览器状态：{desc}。");
            }

            if (!string.IsNullOrWhiteSpace(result.Url))
            {
                Log($"· 浏览器页面：{result.Url}");
            }
            if (!string.IsNullOrWhiteSpace(result.ComposerTag))
            {
                Log($"· 页面输入控件：{result.ComposerTag}");
            }
            return;
        }

        _lblAiBrowser.Text = $"AI 浏览器: 失败 - {desc}";
        _lblAiBrowser.ForeColor = Error;
        Log($"✖ AI 浏览器发送失败：{desc}");
        if (!string.IsNullOrWhiteSpace(preview))
        {
            Log($"· 失败前页面读回：{preview}");
        }
        if (!string.IsNullOrWhiteSpace(result.Url))
        {
            Log($"· 浏览器页面：{result.Url}");
        }
    }

    public MainForm()
    {
        Text = "MyIDE · AI 代码修改助手  v0.2";
        Width = 1500;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgDark;
        ForeColor = FgText;
        Font = new Font("Segoe UI", 9);

        BuildMenu();
        BuildToolbar();
        BuildLayout();

        // 默认示例计划
        _txtPlan.Text = "示例：把 MainForm 的标题改为「我的 AI 编程伙伴 v2」\n然后让窗口宽度变成 1600，高度 950";

        _status.Items.AddRange(new ToolStripItem[] { _lblStatus, new ToolStripStatusLabel(" | ") { ForeColor = FgMuted }, _lblProject, new ToolStripStatusLabel(" | ") { ForeColor = FgMuted }, _lblAiBrowser, new ToolStripStatusLabel(" | ") { ForeColor = FgMuted }, _lblUndo, new ToolStripStatusLabel(" | ") { ForeColor = FgMuted }, _lblCursor });
        
        // 使用统一的主布局容器管理所有控件
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BgDark,
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Menu
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Toolbar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Main content
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Status
        
        mainLayout.Controls.Add(_menu, 0, 0);
        mainLayout.Controls.Add(_toolbar, 0, 1);
        mainLayout.Controls.Add(_mainContent!, 0, 2);
        mainLayout.Controls.Add(_status, 0, 3);
        
        Controls.Add(mainLayout);

        UpdateRecentMenu();
        InitializeSavedJsonContextMenu();
        InitializeSavedCommandContextMenu();

        _txtPlan.Click += UpdateCursorPos;
        _txtPlan.KeyUp += UpdateCursorPos;
        _txtAi.Click += UpdateCursorPos;
        _txtAi.KeyUp += UpdateCursorPos;
        _btnRunSavedCommand.FlatAppearance.BorderSize = 0;
        _btnUseSavedJson.FlatAppearance.BorderSize = 0;
        _btnCopySavedJson.FlatAppearance.BorderSize = 0;
        _btnDeleteSavedJson.FlatAppearance.BorderSize = 0;
        _btnPinSavedJson.FlatAppearance.BorderSize = 0;
        _btnSetBuildCommand.FlatAppearance.BorderSize = 0;
        _btnSetRunCommand.FlatAppearance.BorderSize = 0;
        _btnDeleteSavedCommand.FlatAppearance.BorderSize = 0;
        _btnMoveSavedCommandUp.FlatAppearance.BorderSize = 0;
        _btnMoveSavedCommandDown.FlatAppearance.BorderSize = 0;
        _btnCopySavedCommand.FlatAppearance.BorderSize = 0;
        _btnRunSavedCommand.Click += async (_, _) => await RunSelectedSavedCommandAsync();
        _btnUseSavedJson.Click += (_, _) => UseSelectedSavedJson();
        _btnCopySavedJson.Click += (_, _) => CopySelectedSavedJson();
        _btnDeleteSavedJson.Click += (_, _) => DeleteSelectedSavedJson();
        _btnPinSavedJson.Click += (_, _) => ToggleSelectedSavedJsonPinned();
        _btnSetBuildCommand.Click += (_, _) => SetSelectedSavedCommandAsBuild();
        _btnSetRunCommand.Click += (_, _) => SetSelectedSavedCommandAsRun();
        _btnDeleteSavedCommand.Click += (_, _) => DeleteSelectedSavedCommand();
        _btnMoveSavedCommandUp.Click += (_, _) => MoveSelectedSavedCommand(-1);
        _btnMoveSavedCommandDown.Click += (_, _) => MoveSelectedSavedCommand(1);
        _btnCopySavedCommand.Click += (_, _) => CopySelectedSavedCommand();
        _lstSavedJson.SelectedIndexChanged += (_, _) => UpdateSavedJsonDetail();
        _lstSavedJson.DoubleClick += (_, _) => PreviewSelectedSavedJson();
        _lstSavedJson.MouseDown += SavedJsonList_MouseDown;
        _txtSavedJsonSearch.TextChanged += (_, _) => RefreshSavedJsonPanel();
        _lstSavedCommands.SelectedIndexChanged += (_, _) => UpdateSavedCommandDetail();
        _lstSavedCommands.DoubleClick += async (_, _) => await RunSelectedSavedCommandAsync();
        _lstSavedCommands.MouseDown += SavedCommandsList_MouseDown;
        _lstSavedCommands.KeyDown += SavedCommandsList_KeyDown;
        _txtSavedCommandSearch.TextChanged += (_, _) => RefreshSavedCommandsPanel();
        _lblSavedJsonHeader.Click += (_, _) => ToggleRightPanelSection(RightPanelSection.SavedJson);
        _lblSavedCommandsHeader.Click += (_, _) => ToggleRightPanelSection(RightPanelSection.SavedCommands);
        _lblLogHeader.Click += (_, _) => ToggleRightPanelSection(RightPanelSection.Log);
        _chkAiWrap.CheckedChanged += (_, _) => ApplyAiWrapSetting();
        _tabs.SelectedIndexChanged += UpdateCursorPos;
        _editorTabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateEditorHeader();
            UpdateCursorPos(this, EventArgs.Empty);
            UpdateCursorPos(this, EventArgs.Empty);
        };
        ApplyAiWrapSetting();
        ApplyRightPanelLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // 启动时自动加载最近打开的项目
        if (_settings.RecentDirs.Count > 0)
        {
            var lastDir = _settings.RecentDirs[0];
            if (Directory.Exists(lastDir))
            {
                // 确保在UI显示后加载目录
                BeginInvoke(new Action(() => LoadDirectory(lastDir)));
            }
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

    // ============== 布局构建 ==============

    private void BuildMenu()
    {
        var fileMenu = new ToolStripMenuItem("文件(&F)");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("打开目录(&O)...", null, (_, _) => BtnOpen_Click()) { ShortcutKeys = Keys.Control | Keys.O });
        fileMenu.DropDownItems.Add(_menuRecent);
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("刷新(&R)", null, (_, _) => { if (_projectRoot != null) LoadDirectory(_projectRoot); }));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
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
        var btnOpen = MakeToolButton("📂 打开目录", (_, _) => BtnOpen_Click());
        var btnRefresh = MakeToolButton("🔄 刷新", (_, _) => { if (_projectRoot != null) LoadDirectory(_projectRoot); });
        var btnCloseEditor = MakeToolButton("✖ 关闭代码页", (_, _) => CloseCurrentEditorTab());
        var btnRun = MakeToolButton("▶ 运行 C++", (_, _) => RunCppCode());
        var btnGen = MakeToolButton("✨ 生成提示词", BtnGen_Click);
        var btnCopy = MakeToolButton("📋 复制提示词", (_, _) => CopyPromptToClipboard());
        var btnPreview = MakeToolButton("🔍 预览 Diff", BtnPreview_Click);
        var btnApply = MakeToolButton("✅ 应用", BtnApply_Click);
        var btnUndo = MakeToolButton("↩ 撤销", (_, _) => DoUndo());
        var btnClearLog = MakeToolButton("🧹 清空日志", (_, _) => _txtLog.Clear());

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            btnOpen, btnRefresh, btnCloseEditor, new ToolStripSeparator(),
            btnRun, new ToolStripSeparator(),
            btnGen, btnCopy, new ToolStripSeparator(),
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        root.BackColor = BgDark;

        // 左侧：文件树
        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BgPanel };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _lblTree.Text = "  📁  项目文件";
        _lblTree.Dock = DockStyle.Fill;
        _lblTree.TextAlign = ContentAlignment.MiddleLeft;
        _lblTree.BackColor = BgHeader;
        _lblTree.ForeColor = FgText;
        _lblTree.Font = new Font("Segoe UI", 9, FontStyle.Bold);
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
        leftPanel.Controls.Add(_lblTree, 0, 0);
        leftPanel.Controls.Add(_tree, 0, 1);

        // 中间：标签页
        var planTab = new TabPage("📝 我的计划") { BackColor = BgDark, Padding = new Padding(10) };
        var planPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        planPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        planPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _txtPlan.Dock = DockStyle.Fill;
        _txtPlan.BorderStyle = BorderStyle.FixedSingle;
        planPanel.Controls.Add(_txtPlan, 0, 0);

        var btnSendPlan = new Button
        {
            Text = "✨ 生成并复制提示词",
            Dock = DockStyle.Right,
            Width = 160,
            Height = 32,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.FromArgb(0, 122, 204), // VS Code 蓝
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
        planPanel.Controls.Add(btnSendPlan, 0, 1);
        planTab.Controls.Add(planPanel);
        
        var aiTab = new TabPage("🤖 粘贴 AI 返回 JSON") { BackColor = BgDark, Padding = new Padding(10) };
        var aiPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BgDark,
        };
        aiPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        aiPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _txtAi.Dock = DockStyle.Fill;
        _txtAi.BorderStyle = BorderStyle.FixedSingle;
        aiPanel.Controls.Add(_txtAi, 0, 0);

        var aiBottomPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BgDark
        };
        aiBottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        aiBottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        aiBottomPanel.Controls.Add(_chkAiWrap, 0, 0);

        var btnQuoteContext = new Button
        {
            Text = "📎 引用当前代码和计划",
            Dock = DockStyle.Right,
            Width = 180,
            Height = 32,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnQuoteContext.FlatAppearance.BorderSize = 0;
        btnQuoteContext.Click += BtnQuoteContext_Click;
        aiBottomPanel.Controls.Add(btnQuoteContext, 1, 0);
        aiPanel.Controls.Add(aiBottomPanel, 0, 1);
        aiTab.Controls.Add(aiPanel);

        _tabRunOutput.BackColor = BgDark;
        _tabRunOutput.Padding = new Padding(10);
        var pnlOutputRun = new Panel { Dock = DockStyle.Fill };
        var btnCopyOutput = new Button
        {
            Text = "📋 复制给 AI",
            Dock = DockStyle.Bottom,
            Height = 30,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnCopyOutput.Click += BtnCopyOutput_Click;
        pnlOutputRun.Controls.Add(_txtRunOutput);
        pnlOutputRun.Controls.Add(btnCopyOutput);
        _tabRunOutput.Controls.Add(pnlOutputRun);

        _tabs.TabPages.AddRange(new[] { planTab, aiTab, _tabRunOutput });
        _tabs.Dock = DockStyle.Fill;
        _tabs.BackColor = BgDark;
        _tabs.ForeColor = FgText;

        // 中间上半区：代码查看
        var centerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = (int)(Height * 0.55),
            BackColor = BgHeader,
            SplitterWidth = 4
        };

        var editorPanel = BuildEditorPanel();
        centerSplit.Panel1.Controls.Add(editorPanel);
        centerSplit.Panel2.Controls.Add(_tabs);

        // 右侧：AI JSON + AI 命令收藏 + 日志
        _rightPanel.RowStyles.Clear();
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        _rightPanel.Controls.Clear();
        _lblSavedJsonHeader.Text = "  ▼  AI JSON 历史";
        _savedJsonPanel.RowStyles.Clear();
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        _savedJsonPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _savedJsonPanel.Controls.Clear();
        _txtSavedJsonSearch.Dock = DockStyle.Fill;
        _txtSavedJsonSearch.PlaceholderText = "搜索 AI JSON...";
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
        _savedCommandsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
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
        var savedButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        savedButtonPanel.Controls.Add(_btnRunSavedCommand);
        savedButtonPanel.Controls.Add(_btnCopySavedCommand);
        savedButtonPanel.Controls.Add(_btnMoveSavedCommandUp);
        savedButtonPanel.Controls.Add(_btnMoveSavedCommandDown);
        savedButtonPanel.Controls.Add(_btnSetBuildCommand);
        savedButtonPanel.Controls.Add(_btnSetRunCommand);
        savedButtonPanel.Controls.Add(_btnDeleteSavedCommand);
        _savedCommandsPanel.Controls.Add(savedButtonPanel, 0, 5);
        _lblLogHeader.Text = "  ▼  操作日志";
        _txtLog.Dock = DockStyle.Fill;
        _rightPanel.Controls.Add(_lblSavedJsonHeader, 0, 0);
        _rightPanel.Controls.Add(_savedJsonPanel, 0, 1);
        _rightPanel.Controls.Add(_lblSavedCommandsHeader, 0, 2);
        _rightPanel.Controls.Add(_savedCommandsPanel, 0, 3);
        _rightPanel.Controls.Add(_lblLogHeader, 0, 4);
        _rightPanel.Controls.Add(_txtLog, 0, 5);

        root.Controls.Add(leftPanel, 0, 0);
        root.Controls.Add(centerSplit, 1, 0);
        root.Controls.Add(_rightPanel, 2, 0);

        _mainContent = root;
    }

    private enum RightPanelSection
    {
        SavedJson,
        SavedCommands,
        Log
    }

    /// <summary>
    /// 初始化右侧 AI JSON 历史的右键菜单。
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
        _savedJsonPanel.Visible = !_isSavedJsonCollapsed;
        _savedCommandsPanel.Visible = !_isSavedCommandsCollapsed;
        _txtLog.Visible = !_isLogCollapsed;

        _rightPanel.RowStyles[1].SizeType = _isSavedJsonCollapsed ? SizeType.Absolute : SizeType.Percent;
        _rightPanel.RowStyles[1].Height = _isSavedJsonCollapsed ? 0 : 34;
        _rightPanel.RowStyles[3].SizeType = _isSavedCommandsCollapsed ? SizeType.Absolute : SizeType.Percent;
        _rightPanel.RowStyles[3].Height = _isSavedCommandsCollapsed ? 0 : 38;
        _rightPanel.RowStyles[5].SizeType = _isLogCollapsed ? SizeType.Absolute : SizeType.Percent;
        _rightPanel.RowStyles[5].Height = _isLogCollapsed ? 0 : 28;

        _lblSavedJsonHeader.Text = $"  {(_isSavedJsonCollapsed ? "▶" : "▼")}  AI JSON 历史";
        _lblSavedCommandsHeader.Text = $"  {(_isSavedCommandsCollapsed ? "▶" : "▼")}  AI 命令收藏";
        _lblLogHeader.Text = $"  {(_isLogCollapsed ? "▶" : "▼")}  操作日志";
    }

    /// <summary>
    /// 切换右侧某个区域的折叠状态。
    /// </summary>
    private void ToggleRightPanelSection(RightPanelSection section)
    {
        switch (section)
        {
            case RightPanelSection.SavedJson:
                _isSavedJsonCollapsed = !_isSavedJsonCollapsed;
                break;
            case RightPanelSection.SavedCommands:
                _isSavedCommandsCollapsed = !_isSavedCommandsCollapsed;
                break;
            case RightPanelSection.Log:
                _isLogCollapsed = !_isLogCollapsed;
                break;
        }

        ApplyRightPanelLayout();
    }

    /// <summary>
    /// 右键 AI JSON 时先自动选中当前项，避免菜单作用到旧选择。
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
    /// 刷新右侧 AI JSON 历史区。
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
    }

    /// <summary>
    /// 生成右侧 AI JSON 列表文本，便于快速区分任务和数量。
    /// </summary>
    private static string BuildSavedJsonListText(SavedAiJson item)
    {
        var title = string.IsNullOrWhiteSpace(item.Task) ? "未命名 JSON" : item.Task;
        if (title.Length > 26) title = title[..26] + "...";
        var prefix = item.IsPinned ? "[PIN]" : "[JSON]";
        return $"{prefix} [C{item.ChangeCount}/M{item.CommandCount}] {title}";
    }

    /// <summary>
    /// 刷新右侧 AI JSON 详情。
    /// </summary>
    private void UpdateSavedJsonDetail()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            _txtSavedJsonDetail.Text = "这里会显示最近一次 AI 返回 JSON 的任务摘要和完整内容。";
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

    /// <summary>
    /// 获取当前在右侧选中的 AI JSON 记录。
    /// </summary>
    private SavedAiJson? GetSelectedSavedJson()
    {
        var index = _lstSavedJson.SelectedIndex;
        if (index < 0 || index >= _currentSavedJsonHistory.Count) return null;
        return _currentSavedJsonHistory[index];
    }

    /// <summary>
    /// 将当前 AI 返回 JSON 保存到右侧历史区。
    /// </summary>
    private void SaveAiJsonToSidebar(ChangePlan plan, string jsonText)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot) || string.IsNullOrWhiteSpace(jsonText))
            return;

        var saved = _settings.SaveJsonHistory(new SavedAiJson
        {
            ProjectRoot = _projectRoot,
            Task = plan.Task,
            JsonText = jsonText.Trim(),
            ChangeCount = plan.Changes.Count,
            CommandCount = plan.Commands.Count
        });

        RefreshSavedJsonPanel();
        var index = _currentSavedJsonHistory.FindIndex(c => string.Equals(c.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _lstSavedJson.SelectedIndex = index;
        Log($"✔ 已保存 AI JSON 到右侧历史区：任务={saved.Task}，修改={saved.ChangeCount}，命令={saved.CommandCount}");
    }

    /// <summary>
    /// 把右侧选中的 AI JSON 回填到当前编辑框。
    /// </summary>
    private void UseSelectedSavedJson()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI JSON");
            return;
        }

        _txtAi.Text = item.JsonText;
        _tabs.SelectedIndex = 1;
        _txtAi.Focus();
        _lblStatus.Text = "● 已回填 AI JSON";
        _lblStatus.ForeColor = Accent;
        Log($"✔ 已将右侧 AI JSON 回填到编辑区：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
    }

    /// <summary>
    /// 双击右侧 AI JSON 时，直接回填并打开预览。
    /// </summary>
    private void PreviewSelectedSavedJson()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI JSON");
            return;
        }

        UseSelectedSavedJson();
        PreviewAiJsonText(item.JsonText, saveToSidebar: false);
    }

    /// <summary>
    /// 复制右侧选中的 AI JSON 文本。
    /// </summary>
    private void CopySelectedSavedJson()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI JSON");
            return;
        }

        try
        {
            Clipboard.SetText(item.JsonText);
            Log($"✔ 已复制右侧 AI JSON：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
        }
        catch (Exception ex)
        {
            Warn("复制 AI JSON 失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 删除右侧选中的 AI JSON 记录。
    /// </summary>
    private void DeleteSelectedSavedJson()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI JSON");
            return;
        }

        _settings.RemoveSavedJsonHistory(item.Id);
        RefreshSavedJsonPanel();
        Log($"✔ 已删除右侧 AI JSON：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
    }

    /// <summary>
    /// 切换右侧选中 AI JSON 的置顶状态。
    /// </summary>
    private void ToggleSelectedSavedJsonPinned()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI JSON");
            return;
        }

        var nextPinned = !item.IsPinned;
        _settings.SetSavedJsonPinned(_projectRoot, item.Id, nextPinned);
        RefreshSavedJsonPanel();
        var index = _currentSavedJsonHistory.FindIndex(c => string.Equals(c.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _lstSavedJson.SelectedIndex = index;
        Log(nextPinned
            ? $"✔ 已置顶右侧 AI JSON：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}"
            : $"✔ 已取消置顶右侧 AI JSON：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
    }

    /// <summary>
    /// 解析并预览指定的 AI JSON 文本，供按钮和侧栏双击复用。
    /// </summary>
    private void PreviewAiJsonText(string jsonText, bool saveToSidebar)
    {
        if (_projectRoot == null) { Warn("请先打开目录"); return; }
        if (string.IsNullOrWhiteSpace(jsonText)) { Warn("AI 返回内容为空"); return; }

        try
        {
            var applier = new ChangeApplier(_projectRoot);
            var plan = applier.ParseJson(jsonText);
            if (saveToSidebar)
            {
                SaveAiJsonToSidebar(plan, jsonText);
            }

            var sims = applier.Simulate(plan);
            using var dlg = new DiffPreviewForm(plan.Task, sims) { Owner = this };
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log($"✖ 预览失败：{ex.Message}");
            Warn("解析失败：" + ex.Message);
        }
    }

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
    /// 获取当前在右侧选中的收藏命令。
    /// </summary>
    private SavedAiCommand? GetSelectedSavedCommand()
    {
        var index = _lstSavedCommands.SelectedIndex;
        if (index < 0 || index >= _currentSavedCommands.Count) return null;
        return _currentSavedCommands[index];
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
    /// 初始化右侧收藏命令的右键菜单，减少按钮区来回切换。
    /// </summary>
    private void InitializeSavedCommandContextMenu()
    {
        _savedCommandMenu.ShowImageMargin = false;
        _savedCommandMenu.BackColor = BgPanel;
        _savedCommandMenu.ForeColor = FgText;
        _savedCommandMenu.Items.Add("执行", null, async (_, _) => await RunSelectedSavedCommandAsync());
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
        if (e.KeyCode != Keys.Enter) return;

        e.SuppressKeyPress = true;
        await RunSelectedSavedCommandAsync();
    }

    /// <summary>
    /// 应用 AI 返回 JSON 文本框的自动换行设置。
    /// </summary>
    private void ApplyAiWrapSetting()
    {
        _txtAi.WordWrap = _chkAiWrap.Checked;
        _txtAi.ScrollBars = _chkAiWrap.Checked ? ScrollBars.Vertical : ScrollBars.Both;
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

    /// <summary>
    /// 把文本追加到运行输出页。
    /// </summary>
    private void AppendRunOutput(string text)
    {
        _tabs.SelectedTab = _tabRunOutput;
        _txtRunOutput.AppendText(text);
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
    /// 为 shell 命令构造进程启动信息。
    /// </summary>
    private static System.Diagnostics.ProcessStartInfo BuildShellProcessStartInfo(string shell, string commandText, string workingDirectory)
    {
        var shellName = (shell ?? "powershell").Trim().ToLowerInvariant();
        System.Diagnostics.ProcessStartInfo psi;
        if (shellName is "cmd" or "bat" or "batch")
        {
            psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + commandText);
        }
        else
        {
            var script = "& {\n" + commandText + "\n}\n";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded);
        }

        psi.WorkingDirectory = workingDirectory;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        return psi;
    }

    /// <summary>
    /// 执行收藏命令，并把输出写到运行输出页。
    /// </summary>
    private async Task<bool> ExecuteSavedCommandToRunOutputAsync(SavedAiCommand command, string stageName, int timeoutMs)
    {
        var workingDirectory = ResolveSavedCommandWorkingDirectory(command);
        AppendRunOutput($"[{DateTime.Now:HH:mm:ss}] 使用{stageName}命令：{command.Name}\n");
        AppendRunOutput($"[目录] {workingDirectory}\n");
        AppendRunOutput($"[Shell] {command.Shell}\n");
        AppendRunOutput($"> {command.Command}\n");

        try
        {
            var psi = BuildShellProcessStartInfo(command.Shell, command.Command, workingDirectory);
            var result = await RunProcessCaptureAsync(psi, timeoutMs);

            if (!string.IsNullOrWhiteSpace(result.StdOut)) AppendRunOutput(result.StdOut + "\n");
            if (!string.IsNullOrWhiteSpace(result.StdErr)) AppendRunOutput(result.StdErr + "\n");

            if (result.TimedOut)
            {
                AppendRunOutput($"❌ {stageName}超时：{timeoutMs / 1000} 秒内未完成，已自动终止。\n");
                return false;
            }

            if (result.ExitCode != 0)
            {
                AppendRunOutput($"❌ {stageName}失败，退出码：{result.ExitCode}\n");
                return false;
            }

            AppendRunOutput($"✅ {stageName}成功，退出码：{result.ExitCode}\n");
            Log($"✔ 已执行收藏命令：{command.Name}");
            return true;
        }
        catch (Exception ex)
        {
            AppendRunOutput($"❌ {stageName}异常：{ex.Message}\n");
            return false;
        }
    }

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
    /// 获取当前活动的文本控件，供状态栏显示光标位置。
    /// </summary>
    private TextBoxBase? GetActiveTextControl()
    {
        if (_editorTabs.SelectedTab?.Controls.OfType<RichTextBox>().FirstOrDefault() is RichTextBox editor &&
            (_editorTabs.ContainsFocus || editor.ContainsFocus))
            return editor;

        if (_txtPlan.ContainsFocus || _tabs.SelectedIndex == 0)
            return _txtPlan;

        if (_txtAi.ContainsFocus || _tabs.SelectedIndex == 1)
            return _txtAi;

        return _tabs.SelectedIndex == 0 ? _txtPlan : _txtAi;
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
            "4. 下方可以继续写计划，或粘贴 AI 返回 JSON\r\n\r\n" +
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
            if (sender is RichTextBox box && box.Parent is TabPage tab && tab.Tag is string path)
            {
                try
                {
                    File.WriteAllText(path, box.Text);
                    Log($"✔ 已保存文件：{path}");
                    tab.Text = Path.GetFileName(path);
                    _lblStatus.Text = "● 已保存";
                    _lblStatus.ForeColor = Success;
                }
                catch (Exception ex)
                {
                    Warn($"保存失败：{ex.Message}");
                }
            }
        }
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

    // ============== C++ 编译与运行 ==============

    private string DetectCompiler()
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "clang++", Arguments = "--version", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            proc?.WaitForExit();
            return "clang++";
        }
        catch { }

        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "g++", Arguments = "--version", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            proc?.WaitForExit();
            return "g++";
        }
        catch { }

        return "g++"; // 默认回退到 g++，让它自然抛出找不到文件的异常
    }

    /// <summary>
    /// 执行外部进程，并异步同时收集标准输出和标准错误，避免 ReadToEnd 死锁。
    /// </summary>
    private async System.Threading.Tasks.Task<(int ExitCode, string StdOut, string StdErr, bool TimedOut)> RunProcessCaptureAsync(
        System.Diagnostics.ProcessStartInfo startInfo,
        int timeoutMs)
    {
        using var proc = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                stdOut.AppendLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                stdErr.AppendLine(e.Data);
        };

        if (!proc.Start())
            throw new InvalidOperationException($"无法启动进程：{startInfo.FileName}");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var waitTask = proc.WaitForExitAsync();
        var timeoutTask = System.Threading.Tasks.Task.Delay(timeoutMs);
        var finishedTask = await System.Threading.Tasks.Task.WhenAny(waitTask, timeoutTask);

        if (finishedTask == timeoutTask)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // 忽略超时杀进程时的清理异常
            }

            return (-1, stdOut.ToString(), stdErr.ToString(), true);
        }

        await waitTask;
        return (proc.ExitCode, stdOut.ToString(), stdErr.ToString(), false);
    }

    private async void RunCppCode()
    {
        if (string.IsNullOrEmpty(_projectRoot))
        {
            MessageBox.Show("请先打开一个项目目录！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _tabs.SelectedTab = _tabRunOutput;
        _txtRunOutput.Clear();
        _txtRunOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] 准备编译 C++ 代码...\n");

        var cppFiles = Directory.GetFiles(_projectRoot, "*.cpp", SearchOption.TopDirectoryOnly);
        if (cppFiles.Length == 0)
        {
            _txtRunOutput.AppendText("❌ 错误：在当前目录下未找到任何 .cpp 文件。\n");
            return;
        }

        string exePath = Path.Combine(_projectRoot, "app_run.exe");
        string filesArgs = string.Join(" ", cppFiles.Select(f => $"\"{Path.GetFileName(f)}\""));

        try
        {
            var savedBuildCommand = _settings.GetDefaultBuildCommand(_projectRoot);
            var savedRunCommand = _settings.GetDefaultRunCommand(_projectRoot);

            if (savedBuildCommand != null)
            {
                AppendRunOutput("📌 已使用右侧置顶的默认编译命令。\n");
                var buildOk = await ExecuteSavedCommandToRunOutputAsync(savedBuildCommand, "编译", 60_000);
                if (!buildOk) return;
            }
            else
            {
                string compiler = DetectCompiler();
                _txtRunOutput.AppendText($"> {compiler} {filesArgs} -o app_run.exe\n");

                var buildInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = compiler,
                    Arguments = $"{filesArgs} -o app_run.exe",
                    WorkingDirectory = _projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var buildResult = await RunProcessCaptureAsync(buildInfo, timeoutMs: 60_000);

                if (!string.IsNullOrEmpty(buildResult.StdOut)) AppendRunOutput(buildResult.StdOut + "\n");
                if (!string.IsNullOrEmpty(buildResult.StdErr)) AppendRunOutput(buildResult.StdErr + "\n");

                if (buildResult.TimedOut)
                {
                    AppendRunOutput("❌ 编译超时：60 秒内未完成，已自动终止编译进程。\n");
                    return;
                }

                if (buildResult.ExitCode != 0)
                {
                    AppendRunOutput($"❌ 编译失败，退出码：{buildResult.ExitCode}\n");
                    return;
                }
            }

            AppendRunOutput(new string('-', 40) + "\n");

            if (savedRunCommand != null)
            {
                AppendRunOutput("📌 已使用右侧置顶的默认运行命令。\n");
                await ExecuteSavedCommandToRunOutputAsync(savedRunCommand, "运行", 30_000);
                return;
            }

            if (!File.Exists(exePath))
            {
                AppendRunOutput("⚠ 当前未设置默认运行命令，且未找到 app_run.exe，已结束。\n");
                return;
            }

            AppendRunOutput("✅ 编译成功，开始运行...\n" + new string('-', 40) + "\n");

            var runInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var runResult = await RunProcessCaptureAsync(runInfo, timeoutMs: 30_000);

            if (!string.IsNullOrEmpty(runResult.StdOut)) AppendRunOutput(runResult.StdOut);
            if (!string.IsNullOrEmpty(runResult.StdErr)) AppendRunOutput(runResult.StdErr);

            if (runResult.TimedOut)
            {
                AppendRunOutput("\n" + new string('-', 40) + "\n⚠ 运行超时：30 秒内未退出，已自动终止程序。\n");
                return;
            }

            AppendRunOutput("\n" + new string('-', 40) + $"\n✅ 运行结束，退出码：{runResult.ExitCode}\n");
        }
        catch (Exception ex)
        {
            AppendRunOutput($"❌ 发生异常：{ex.Message}\n");
        }
    }

    private async void BtnCopyOutput_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtRunOutput.Text)) return;

        // 生成带有当前文件和计划的基础提示词
        string prompt = GeneratePromptText(out var files) ?? "";

        string content = prompt;
        if (!string.IsNullOrWhiteSpace(content))
        {
            content += "\n\n";
        }
        
        content += "======================================\n";
        content += "以下是程序的最新编译和运行结果：\n\n```text\n" + _txtRunOutput.Text + "\n```\n\n";
        content += "请结合上述代码和运行结果，分析并修复问题，或者继续完成下一步计划。请严格返回同一份 JSON：必须同时包含 changes 和 commands 两个字段；如果没有命令也要返回 \"commands\": []。不要在 JSON 外输出解释、Markdown 代码块或额外文字。";

        try
        {
            Clipboard.SetText(content);
            Log("✔ 已将提示词和运行输出组合复制到剪贴板，可直接粘贴给 AI。");
            var browserResult = await TrySendToAiBrowserAsync(content, autoSend: true, source: "run_output");
            if (browserResult.Ok)
            {
                UpdateAiBrowserSendStatus(browserResult, content);
            }
            else
            {
                UpdateAiBrowserSendStatus(browserResult, content);
                Log("· 已保留剪贴板兜底。请确认 MyWebView2Browser 已启动并打开 AI 页面。");
            }
            MessageBox.Show("已包含当前提示词和运行结果，复制成功！\n请直接粘贴给 AI。", "复制成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Warn($"复制失败：{ex.Message}");
        }
    }

    // ============== 事件处理 ==============

    private void BtnOpen_Click()
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && Directory.Exists(dlg.SelectedPath))
            LoadDirectory(dlg.SelectedPath);
    }

    private void LoadDirectory(string path)
    {
        _projectRoot = path;
        _tree.Nodes.Clear();
        ResetEditorTabs();
        RefreshSavedJsonPanel();
        RefreshSavedCommandsPanel();
        var rootNode = new TreeNode("📁  " + Path.GetFileName(path)) { Tag = path, ForeColor = FgText };
        _tree.Nodes.Add(rootNode);
        try
        {
            AddDirectoryNodes(rootNode, path, depth: 0);
            rootNode.Expand();
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

    private static readonly string[] _skipDirs = new[]
    {
        ".git", ".vs", "bin", "obj", "node_modules", "target", "build", "dist", ".idea", ".vscode",
        "__pycache__", "venv", ".venv"
    };

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
                if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                var node = new TreeNode("📄  " + name) { Tag = file, ForeColor = FgText };
                parent.Nodes.Add(node);
            }
        }
        catch { /* 忽略无权限目录 */ }
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
            _tabs.SelectedIndex = 0;
            Log($"已打开文件：{rel}，现在可以在上方查看源码，并继续生成提示词");
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
                var rel = Path.GetRelativePath(_projectRoot, p).Replace('\\', '/');
                if (!list.Contains(rel)) list.Add(rel);
            }
        }

        // 3. 如果还是没有，获取当前在树中选中的文件
        if (list.Count == 0 && GetCurrentSelectedFile() is string sel && !list.Contains(sel))
        {
            list.Add(sel);
        }

        return list;
    }

    private string? GetCurrentSelectedFile()
    {
        if (_tree.SelectedNode?.Tag is string p && File.Exists(p) && _projectRoot != null)
            return Path.GetRelativePath(_projectRoot, p).Replace('\\', '/');
        return null;
    }

    /// <summary>
    /// 生成人工智能提示词，并弹窗展示，方便用户复制给外部 AI。
    /// </summary>
    private void BtnGen_Click(object? sender, EventArgs e)
    {
        if (_projectRoot == null) { Warn("请先打开一个目录"); return; }
        var prompt = GeneratePromptText(out var files);
        if (prompt == null) return;

        ShowPromptWindow(prompt);
        _lblStatus.Text = "● 提示词已生成";
        _lblStatus.ForeColor = Success;
        Log($"✔ 已生成提示词（{prompt.Length} 字符，带入了 {files.Count} 个文件）");
        Log("下一步：复制提示词给 AI，把返回的 JSON 粘贴到“AI 返回 JSON”页，再点“应用”");
    }

    /// <summary>
    /// 展示提示词窗口，并提供一键复制能力。
    /// </summary>
    private void ShowPromptWindow(string prompt)
    {
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
            Text = prompt,
            ReadOnly = true,
            BackColor = BgDark,
            ForeColor = FgText,
            BorderStyle = BorderStyle.None,
        };
        var btn = new Button
        {
            Text = "📋 复制到剪贴板",
            Dock = DockStyle.Bottom,
            Height = 40,
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(prompt);
                btn.Text = "✅ 已复制！";
                _tabs.SelectedIndex = 1;
                Log("✔ 已复制提示词，请粘贴给 AI；拿到 JSON 后粘回当前页并点“应用”");
            }
            catch (Exception ex) { MessageBox.Show("复制失败：" + ex.Message); }
        };
        dlg.Controls.Add(tb);
        dlg.Controls.Add(btn);
        dlg.ShowDialog(this);
    }

    /// <summary>
    /// 生成提示词并缓存到窗体状态，供展示和复制复用。
    /// </summary>
    private string? GeneratePromptText(out List<string> files)
    {
        files = new List<string>();
        if (_projectRoot == null) return null;

        var gen = new PromptGenerator();
        files = GetCheckedFiles();
        var prompt = gen.Generate(_projectRoot, files, _txtPlan.Text, _chkIncludeAll.Checked);
        _lastGeneratedPrompt = prompt;
        return prompt;
    }

    /// <summary>
    /// 复制最近一次生成的提示词；如果还没生成，则即时生成后再复制。
    /// </summary>
    private async void CopyPromptToClipboard()
    {
        if (_projectRoot == null) { Warn("请先打开目录"); return; }
        var prompt = _lastGeneratedPrompt;
        var files = GetCheckedFiles();
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
            Log("下一步：把提示词发给 AI，然后将返回 JSON 粘贴到“AI 返回 JSON”页");
            _tabs.SelectedIndex = 1;
            _lblStatus.Text = "● 等待粘贴 AI JSON";
            _lblStatus.ForeColor = Accent;
        }
        catch (Exception ex) { Log($"✖ 复制失败：{ex.Message}"); }
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

    /// <summary>
    /// 在粘贴 JSON 页快速复制当前代码上下文和计划，方便重新发给 AI。
    /// </summary>
    private void BtnQuoteContext_Click(object? sender, EventArgs e)
    {
        CopyPromptToClipboard();
        Log("✔ 已通过“引用”按钮复制当前代码和计划，可直接发给 AI。");
    }

    /// <summary>解析 AI 返回，模拟应用，弹出 diff 预览</summary>
    private void BtnPreview_Click(object? sender, EventArgs e)
    {
        PreviewAiJsonText(_txtAi.Text, saveToSidebar: true);
    }

    /// <summary>解析 → 模拟 → 弹出 diff → 用户确认 → 应用</summary>
    private void BtnApply_Click(object? sender, EventArgs e)
    {
        if (_projectRoot == null) { Warn("请先打开目录"); return; }
        if (string.IsNullOrWhiteSpace(_txtAi.Text)) { Warn("AI 返回内容为空"); return; }

        ChangeApplier applier;
        ChangePlan plan;
        List<ChangeApplier.SimulatedFile> sims;
        try
        {
            applier = new ChangeApplier(_projectRoot);
            plan = applier.ParseJson(_txtAi.Text);
            SaveAiJsonToSidebar(plan, _txtAi.Text);
        }
        catch (Exception ex)
        {
            Log($"✖ 解析失败：{ex.Message}");
            Warn("解析失败：" + ex.Message);
            return;
        }

        if (plan.Changes.Count == 0 && plan.Commands.Count > 0)
        {
            Log($"· 本次 JSON 没有文件修改，直接进入命令执行流程：命令数={plan.Commands.Count}");
            _lblStatus.Text = "● 仅包含命令";
            _lblStatus.ForeColor = Accent;
            using var commandOnlyForm = new AiCommandBatchForm(_projectRoot, plan.Commands, SaveAiCommandsToSidebar) { Owner = this };
            commandOnlyForm.ShowDialog(this);
            return;
        }

        sims = applier.Simulate(plan);

        Log($"· 打开差异预览：任务={plan.Task}，文件数={sims.Count}，命令数={plan.Commands.Count}");
        foreach (var sim in sims)
        {
            foreach (var issue in sim.Issues)
            {
                Log($"[预览] {sim.RelativePath} {issue}");
            }
        }
        foreach (var command in plan.Commands)
        {
            var name = string.IsNullOrWhiteSpace(command.Name) ? command.Command : command.Name;
            Log($"[命令] {name} | 目录={command.WorkingDirectory} | Shell={command.Shell}");
        }

        using var dlg = new DiffPreviewForm(plan.Task, sims) { Owner = this };
        var dialogResult = dlg.ShowDialog(this);
        Log($"· 差异预览关闭：DialogResult={dialogResult}，Accepted={dlg.Accepted}");
        if (dialogResult != DialogResult.OK || !dlg.Accepted)
        {
            Log("· 用户取消了应用");
            return;
        }

        // 先记录撤销快照
        var snap = _undo.BeginSnapshot(plan.Task);
        foreach (var sim in sims)
        {
            var fullPath = Path.Combine(_projectRoot, sim.RelativePath);
            if (File.Exists(fullPath)) _undo.RecordFile(snap, fullPath);
        }
        _undo.Commit(snap);

        // 再真正落盘
        Log($"· 开始写盘：项目根={_projectRoot}");
        var summary = applier.Apply(plan, _chkBackup.Checked);
        RefreshOpenEditors();
        UpdateUndoLabel();
        Log("======== 应用结果 ========");
        Log(summary.ToString());
        _lblStatus.Text = $"● 完成 {summary.SuccessOps}/{summary.TotalOps}";
        _lblStatus.ForeColor = summary.SuccessOps == summary.TotalOps ? Success : Warning;
        Log($"✔ 已记录撤销点（可撤销 {_undo.UndoCount} 步）");

        if (plan.Commands.Count > 0)
        {
            Log($"· AI 还返回了 {plan.Commands.Count} 条命令，准备打开命令执行窗口。");
            using var commandForm = new AiCommandBatchForm(_projectRoot, plan.Commands, SaveAiCommandsToSidebar) { Owner = this };
            commandForm.ShowDialog(this);
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
            "  5. 把 AI 返回的 JSON 粘到「AI 返回 JSON」\n" +
            "  6. 点「预览 Diff」查看修改，再点「应用」落盘\n" +
            "  7. 如果 JSON 里还带有 commands，会在应用后弹出命令执行窗口\n" +
            "  8. 应用或撤销后，已打开的代码页会自动刷新\n\n" +
            "JSON 规范（提示词里已自动注入）：\n" +
            "  type: replace | insert | delete\n" +
            "  start/end: 起始/结束行号（1-based，含）\n" +
            "  after: 在第 N 行后插入（insert 用）\n" +
            "  content: 新内容（\\n 表示换行）\n" +
            "  commands: 应用后可执行的命令列表",
            "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Log(string msg)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Log), msg);
            return;
        }
        _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    private void Warn(string msg)
    {
        Log($"⚠ {msg}");
        MessageBox.Show(this, msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
