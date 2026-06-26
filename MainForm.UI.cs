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

public partial class MainForm
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
    private static readonly HttpClient AiBrowserLatestReplyHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    private static readonly HttpClient DebugHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private string? _projectRoot;
    private string _lastGeneratedPrompt = "";
    
    // C++ 运行输出相关的控件
    // （已移除无用的标签页字段）
    private RichTextBox _txtRunOutput = new RichTextBox
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.FromArgb(200, 200, 200),
        Font = new Font("Cascadia Mono", 10),
        ReadOnly = true,
        BorderStyle = BorderStyle.None
    };
    private TextBox _txtTerminalInput = new TextBox
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(40, 40, 40),
        ForeColor = Color.FromArgb(220, 220, 220),
        Font = new Font("Cascadia Mono", 10),
        BorderStyle = BorderStyle.FixedSingle,
        PlaceholderText = "在此输入命令 (回车执行)"
    };
    private readonly Label _lblRunSessionState = new()
    {
        AutoSize = true,
        ForeColor = FgMuted,
        Text = "空闲",
        Margin = new Padding(12, 0, 0, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 9, FontStyle.Bold)
    };
    private readonly Button _btnStopRunOutput = new()
    {
        Text = "■ 停止",
        Width = 86,
        Height = 32,
        BackColor = Color.FromArgb(110, 50, 50),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Enabled = false
    };
    private readonly Button _btnClearRunOutput = new()
    {
        Text = "🧹 清空输出",
        Width = 110,
        Height = 32,
        BackColor = Color.FromArgb(90, 45, 45),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly CheckBox _chkAutoScrollOutput = new()
    {
        AutoSize = true,
        Checked = true,
        Text = "输出自动滚动",
        ForeColor = FgMuted,
        BackColor = BgPanel,
        Margin = new Padding(4, 7, 8, 0)
    };
    private readonly CheckBox _chkAutoScrollLog = new()
    {
        AutoSize = true,
        Checked = true,
        Text = "日志自动滚动",
        ForeColor = FgMuted,
        BackColor = BgPanel,
        Margin = new Padding(0, 7, 0, 0)
    };
    private string _terminalCwd = "";
    private System.Diagnostics.Process? _activeRunProcess;
    private string _activeRunProcessDisplayName = "";
    private bool _activeRunStopRequested;
    private readonly object _activeRunProcessSync = new();
    private readonly UndoStack _undo = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _suppressPanelSplitterSave;

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
    private readonly TextBox _txtPlan = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Cascadia Mono", 10), BorderStyle = BorderStyle.None, BackColor = BgDark, ForeColor = FgText };
    private readonly TextBox _txtAi = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = new Font("Cascadia Mono", 10), BorderStyle = BorderStyle.None, BackColor = BgDark, ForeColor = FgText };

    // 右侧：日志
    private readonly ListBox _lstSavedPlans = new()
    {
        BackColor = BgDark,
        ForeColor = FgText,
        BorderStyle = BorderStyle.None,
        Font = new Font("Cascadia Mono", 9),
        HorizontalScrollbar = true
    };
    private readonly Label _lblSavedPlansSummary = new()
    {
        Text = "  暂无计划历史",
        ForeColor = FgMuted,
        BackColor = BgPanel,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 8, FontStyle.Bold)
    };
    private readonly TextBox _txtSavedPlanSearch = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = BgDark,
        ForeColor = FgText,
        Font = new Font("Segoe UI", 9)
    };
    private readonly TextBox _txtSavedPlanDetail = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = BgDark,
        ForeColor = FgText,
        BorderStyle = BorderStyle.None,
        Font = new Font("Cascadia Mono", 9)
    };
    private readonly Button _btnUseSavedPlan = new()
    {
        Text = "回填",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(0, 122, 204),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnCopySavedPlan = new()
    {
        Text = "复制",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnPinSavedPlan = new()
    {
        Text = "置顶",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly Button _btnDeleteSavedPlan = new()
    {
        Text = "删除",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(90, 45, 45),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
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
        Text = "  暂无 AI 返回内容",
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
    private readonly Button _btnEditSavedCommand = new()
    {
        Text = "编辑",
        Width = 62,
        Height = 28,
        BackColor = Color.FromArgb(60, 60, 60),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };
    private readonly ContextMenuStrip _savedJsonMenu = new();
    private readonly ContextMenuStrip _savedCommandMenu = new();
    private readonly TabControl _bottomOutputTabs = new()
    {
        Dock = DockStyle.Fill,
        DrawMode = TabDrawMode.OwnerDrawFixed,
        SizeMode = TabSizeMode.Fixed,
        ItemSize = new Size(140, 28),
        Padding = new Point(12, 4)
    };
    private readonly TabPage _tabRunOutput = new() { Text = "命令输出", BackColor = BgDark };
    private readonly TabPage _tabOperationLog = new() { Text = "操作日志 [0]", BackColor = BgDark };
    private readonly TextBox _txtLog = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = BgDark, ForeColor = Success, Font = new Font("Cascadia Mono", 9), BorderStyle = BorderStyle.None };
    private int _pendingOperationLogCount;
    private readonly TableLayoutPanel _rightPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = BgPanel };
    private readonly SplitContainer _leftCenterSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,
        SplitterWidth = 4,
        BackColor = BgHeader
    };
    private readonly SplitContainer _centerRightSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,
        SplitterWidth = 4,
        BackColor = BgHeader
    };
    private readonly TableLayoutPanel _savedPlanPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = BgPanel, Padding = new Padding(6) };
    private readonly TableLayoutPanel _savedJsonPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = BgPanel, Padding = new Padding(6) };
    private readonly TableLayoutPanel _savedCommandsPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = BgPanel, Padding = new Padding(6) };
    private readonly Label _lblSavedPlanHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private readonly Label _lblSavedJsonHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private readonly Label _lblSavedCommandsHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private readonly System.Windows.Forms.Timer _aiJsonSidebarSyncTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _planSaveTimer = new() { Interval = 700 };
    private readonly ContextMenuStrip _treeMenu = new();
    private readonly ContextMenuStrip _savedPlanMenu = new();
    private List<SavedAiJson> _currentSavedJsonHistory = new();
    private List<SavedPlanHistory> _currentSavedPlans = new();
    private List<SavedAiCommand> _currentSavedCommands = new();
    private bool _isSavedPlanCollapsed;
    private bool _isSavedJsonCollapsed;
    private bool _isSavedCommandsCollapsed;
    private string _lastSidebarSyncedAiJson = "";
    private string _lastSavedPlanHistoryText = "";
    private DateTime _lastPlanHistorySavedAt = DateTime.MinValue;

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
    /// AI 浏览器桥接服务返回的最新回复抓取结果。
    /// </summary>
    private sealed class AiBrowserReplyResult
    {
        public bool Ok { get; set; }
        public string Reason { get; set; } = "";
        public string Url { get; set; } = "";
        public string Source { get; set; } = "";
        public string ReplyText { get; set; } = "";
        public string Signature { get; set; } = "";
        public string CopyMethod { get; set; } = "";
        public string CopyButtonHint { get; set; } = "";
        public string Message { get; set; } = "";
        public string DebugInfo { get; set; } = "";
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
        _isSavedPlanCollapsed = _settings.IsSavedPlanCollapsed;
        _isSavedJsonCollapsed = _settings.IsSavedJsonCollapsed;
        _isSavedCommandsCollapsed = _settings.IsSavedCommandsCollapsed;

        // 默认示例计划
        _txtPlan.Text = string.IsNullOrWhiteSpace(_settings.LastPlanText)
            ? "示例：把 MainForm 的标题改为「我的 AI 编程伙伴 v2」\n然后让窗口宽度变成 1600，高度 950"
            : _settings.LastPlanText;

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
        InitializeSavedPlanContextMenu();
        InitializeSavedJsonContextMenu();
        InitializeSavedCommandContextMenu();
        UpdateAiJsonBufferStatus();

        _txtPlan.Click += UpdateCursorPos;
        _txtPlan.KeyUp += UpdateCursorPos;
        _txtPlan.TextChanged += (_, _) => SchedulePlanTextSave();
        _txtPlan.Leave += (_, _) => SaveCurrentPlanText(forceHistory: true);
        _txtAi.TextChanged += (_, _) => UpdateAiJsonBufferStatus();
        _btnUseSavedPlan.FlatAppearance.BorderSize = 0;
        _btnCopySavedPlan.FlatAppearance.BorderSize = 0;
        _btnPinSavedPlan.FlatAppearance.BorderSize = 0;
        _btnDeleteSavedPlan.FlatAppearance.BorderSize = 0;
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
        _btnStopRunOutput.Click += (_, _) => StopActiveRunProcess();
        _btnClearRunOutput.Click += (_, _) => _txtRunOutput.Clear();
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
        _btnEditSavedCommand.Click += (_, _) => EditSelectedSavedCommand();
        _btnUseSavedPlan.Click += (_, _) => UseSelectedSavedPlan();
        _btnCopySavedPlan.Click += (_, _) => CopySelectedSavedPlan();
        _btnPinSavedPlan.Click += (_, _) => ToggleSelectedSavedPlanPinned();
        _btnDeleteSavedPlan.Click += (_, _) => DeleteSelectedSavedPlan();
        _lstSavedPlans.SelectedIndexChanged += (_, _) => UpdateSavedPlanDetail();
        _lstSavedPlans.DoubleClick += (_, _) => UseSelectedSavedPlan();
        _lstSavedPlans.MouseDown += SavedPlanList_MouseDown;
        _lstSavedPlans.KeyDown += SavedPlanList_KeyDown;
        _txtSavedPlanSearch.TextChanged += (_, _) => RefreshSavedPlanPanel();
        _lstSavedJson.SelectedIndexChanged += (_, _) => UpdateSavedJsonDetail();
        _lstSavedJson.DoubleClick += (_, _) => PreviewSelectedSavedJson();
        _lstSavedJson.MouseDown += SavedJsonList_MouseDown;
        _lstSavedJson.KeyDown += SavedJsonList_KeyDown;
        _txtSavedJsonSearch.TextChanged += (_, _) => RefreshSavedJsonPanel();
        _lstSavedCommands.SelectedIndexChanged += (_, _) => UpdateSavedCommandDetail();
        _lstSavedCommands.DoubleClick += async (_, _) => await RunSelectedSavedCommandAsync();
        _lstSavedCommands.MouseDown += SavedCommandsList_MouseDown;
        _lstSavedCommands.KeyDown += SavedCommandsList_KeyDown;
        _txtSavedCommandSearch.TextChanged += (_, _) => RefreshSavedCommandsPanel();
        _lblSavedPlanHeader.Click += (_, _) => ToggleRightPanelSection(RightPanelSection.SavedPlan);
        _lblSavedJsonHeader.Click += (_, _) => ToggleRightPanelSection(RightPanelSection.SavedJson);
        _lblSavedCommandsHeader.Click += (_, _) => ToggleRightPanelSection(RightPanelSection.SavedCommands);
        _bottomOutputTabs.DrawItem += BottomOutputTabs_DrawItem;
        _bottomOutputTabs.SelectedIndexChanged += (_, _) => HandleBottomOutputTabChanged();
        _chkAiWrap.CheckedChanged += (_, _) => ApplyAiWrapSetting();
        _txtAi.TextChanged += (_, _) => ScheduleAiJsonSidebarSync();
        _txtAi.Leave += (_, _) => TrySaveCurrentAiJsonToSidebar();
        _aiJsonSidebarSyncTimer.Tick += (_, _) =>
        {
            _aiJsonSidebarSyncTimer.Stop();
            TrySaveCurrentAiJsonToSidebar();
        };
        _planSaveTimer.Tick += (_, _) =>
        {
            _planSaveTimer.Stop();
            SaveCurrentPlanText();
        };
        _leftCenterSplit.SplitterMoved += PanelSplitter_SplitterMoved;
        _centerRightSplit.SplitterMoved += PanelSplitter_SplitterMoved;
        FormClosing += (_, _) =>
        {
            SavePanelSplitterSettings();
            SaveCurrentPlanText();
            StopAiReplyImportServer();
        };
        // _tabsLeft.SelectedIndexChanged += UpdateCursorPos;
        // _tabsRight.SelectedIndexChanged += UpdateCursorPos;
        _editorTabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateEditorHeader();
            UpdateCursorPos(this, EventArgs.Empty);
            UpdateCursorPos(this, EventArgs.Empty);
        };
        ApplyAiWrapSetting();
        ApplyRightPanelLayout();
        RefreshSavedPlanPanel();
        UpdateRunSessionState("空闲", FgMuted, false);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        StartAiReplyImportServer();
        BeginInvoke(new Action(ApplySplitterSettings));

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



    // ============== 布局构建 ==============



































    /// <summary>
    /// 当通用字符级修复失败时，按 JSON 属性行重建字符串值，适合修复 content 里的多行源码。
    /// </summary>
    private static bool TryRepairRenderedAiJsonByPropertyLines(string text, out string repairedText)
    {
        repairedText = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(repairedText)) return false;

        var normalized = repairedText.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var sb = new StringBuilder(normalized.Length + 128);
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var match = Regex.Match(line, @"^(?<prefix>\s*""[^""]+""\s*:\s*"")(?<rest>.*)$");
            if (!match.Success)
            {
                sb.AppendLine(line);
                i++;
                continue;
            }

            var prefix = match.Groups["prefix"].Value;
            var current = match.Groups["rest"].Value;
            var contentBuilder = new StringBuilder();
            string suffix = "";
            var closed = false;
            var j = i;

            while (j < lines.Length)
            {
                var part = j == i ? current : lines[j];
                var closeIndex = FindLikelyClosingQuoteIndex(part, lines, j);
                if (closeIndex >= 0)
                {
                    contentBuilder.Append(part[..closeIndex]);
                    suffix = part[(closeIndex + 1)..];
                    closed = true;
                    break;
                }

                if (contentBuilder.Length > 0)
                {
                    contentBuilder.Append('\n');
                }
                contentBuilder.Append(part);
                j++;
            }

            if (!closed)
            {
                return false;
            }

            var escaped = EscapeJsonStringContent(contentBuilder.ToString());
            sb.Append(prefix);
            sb.Append(escaped);
            sb.Append('"');
            sb.AppendLine(suffix);
            i = j + 1;
        }

        var candidate = sb.ToString().Trim();
        if (!CanParseJson(candidate)) return false;

        repairedText = candidate;
        return true;
    }

    /// <summary>
    /// 查找当前行中更像 JSON 字符串结束符的双引号位置，结合下一行的 JSON 结构判断。
    /// </summary>
    private static int FindLikelyClosingQuoteIndex(string line, string[] lines, int lineIndex)
    {
        for (var i = line.Length - 1; i >= 0; i--)
        {
            if (line[i] != '"') continue;

            var suffix = line[(i + 1)..];
            if (!Regex.IsMatch(suffix, @"^\s*,?\s*$")) continue;

            var next = "";
            for (var j = lineIndex + 1; j < lines.Length; j++)
            {
                if (!string.IsNullOrWhiteSpace(lines[j]))
                {
                    next = lines[j].TrimStart();
                    break;
                }
            }

            if (string.IsNullOrEmpty(next) ||
                next.StartsWith("\"", StringComparison.Ordinal) ||
                next.StartsWith("}", StringComparison.Ordinal) ||
                next.StartsWith("]", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 把渲染后的多行文本重新编码成合法 JSON 字符串内容。
    /// </summary>
    private static string EscapeJsonStringContent(string text)
    {
        var source = text ?? string.Empty;
        var sb = new StringBuilder(source.Length + 32);

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            if (ch == '\\')
            {
                if (i + 1 < source.Length && IsExistingJsonEscapeLead(source[i + 1]))
                {
                    sb.Append('\\');
                    sb.Append(source[i + 1]);
                    i++;
                    continue;
                }

                sb.Append(@"\\");
                continue;
            }

            if (ch == '"')
            {
                sb.Append("\\\"");
                continue;
            }

            if (ch == '\r')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n')
                {
                    i++;
                }
                sb.Append("\\n");
                continue;
            }

            if (ch == '\n')
            {
                sb.Append("\\n");
                continue;
            }

            if (ch == '\t')
            {
                sb.Append("\\t");
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 判断反斜杠后面的字符是否已经是合法 JSON 转义前缀，避免重复转义。
    /// </summary>
    private static bool IsExistingJsonEscapeLead(char ch)
    {
        return ch is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u';
    }



    #region debug-point C:report-helper



    #endregion












    /// <summary>
    /// 解析 MyChrome 浏览器项目文件路径，优先走当前仓库相对路径，再回退到固定路径。
    /// </summary>
    private string? ResolveMyChromeProjectPath()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\python\my_chrome\MyWebView2Browser\MyWebView2Browser.csproj")),
            @"E:\GitHub3\python\my_chrome\MyWebView2Browser\MyWebView2Browser.csproj"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 查找 MyChrome 已编译好的可执行文件，优先 Release，再回退 Debug。
    /// </summary>
    private string? FindMyChromeExecutable(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory)) return null;

        var myIdeReleaseDir = Path.Combine(projectDirectory, "bin", "Release_myide");
        if (Directory.Exists(myIdeReleaseDir))
        {
            var myIdeReleaseExe = Directory
                .EnumerateFiles(myIdeReleaseDir, "MyWebView2Browser.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(myIdeReleaseExe)) return myIdeReleaseExe;
        }

        var releaseDir = Path.Combine(projectDirectory, "bin", "Release");
        if (Directory.Exists(releaseDir))
        {
            var releaseExe = Directory
                .EnumerateFiles(releaseDir, "MyWebView2Browser.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(releaseExe)) return releaseExe;
        }

        var debugDir = Path.Combine(projectDirectory, "bin", "Debug");
        if (!Directory.Exists(debugDir)) return null;

        return Directory
            .EnumerateFiles(debugDir, "MyWebView2Browser.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    /// <summary>
    /// 判断 MyChrome 当前可执行文件是否落后于项目源码或脚本资源。
    /// </summary>
    private static bool IsMyChromeBuildOutdated(string projectPath, string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return true;

            var projectDirectory = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory)) return true;

            var exeWriteTime = File.GetLastWriteTimeUtc(exePath);
            var latestSourceWriteTime = Directory
                .EnumerateFiles(projectDirectory, "*.*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var ext = Path.GetExtension(path);
                    return ext.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".resx", StringComparison.OrdinalIgnoreCase);
                })
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            return latestSourceWriteTime > exeWriteTime;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 编译 MyChrome 浏览器项目，并把编译结果写入 MyIDE 日志区。
    /// </summary>
    private async Task<bool> BuildMyChromeAsync(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? "";
        var outputDirectory = Path.Combine(projectDirectory, "bin", "Release_myide");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release -o \"{outputDirectory}\"",
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var result = await RunProcessCaptureAsync(psi, timeoutMs: 120_000, displayName: "编译 MyChrome");
        if (!string.IsNullOrWhiteSpace(result.StdOut)) Log("[MyChrome] " + result.StdOut.Trim());
        if (!string.IsNullOrWhiteSpace(result.StdErr)) Log("[MyChrome] " + result.StdErr.Trim());

        if (result.TimedOut)
        {
            Warn("MyChrome 编译超时，120 秒内未完成。");
            return false;
        }

        if (result.StoppedByUser)
        {
            Log("⚠ MyChrome 编译已被手动停止。");
            return false;
        }

        if (result.ExitCode != 0)
        {
            Warn($"MyChrome 编译失败，退出码：{result.ExitCode}");
            return false;
        }

        Log("✔ MyChrome 编译成功。");
        return true;
    }



    private enum RightPanelSection
    {
        SavedPlan,
        SavedJson,
        SavedCommands
    }

    private enum AiJsonDialogAction
    {
        None,
        Save,
        Preview,
        Apply
    }





























    /// <summary>
    /// 生成右侧计划历史列表文本，便于快速区分时间和标题。
    /// </summary>
    private static string BuildSavedPlanListText(SavedPlanHistory item)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) ? "未命名计划" : item.Title;
        if (title.Length > 24) title = title[..24] + "...";
        var prefix = item.IsPinned ? "[PIN]" : "[PLAN]";
        return $"{prefix} {item.UpdatedAt:MM-dd HH:mm} {title}";
    }

    /// <summary>
    /// 获取当前在右侧选中的计划历史记录。
    /// </summary>
    private SavedPlanHistory? GetSelectedSavedPlan()
    {
        var index = _lstSavedPlans.SelectedIndex;
        if (index < 0 || index >= _currentSavedPlans.Count) return null;
        return _currentSavedPlans[index];
    }

    /// <summary>
    /// 生成右侧 AI 返回历史列表文本，便于快速区分任务和数量。
    /// </summary>
    private static string BuildSavedJsonListText(SavedAiJson item)
    {
        var title = string.IsNullOrWhiteSpace(item.Task) ? "未命名 JSON" : item.Task;
        if (title.Length > 26) title = title[..26] + "...";
        var prefix = item.IsPinned ? "[PIN]" : "[JSON]";
        return $"{prefix} [C{item.ChangeCount}/M{item.CommandCount}] {title}";
    }



    /// <summary>
    /// 获取当前在右侧选中的 AI 返回内容记录。
    /// </summary>
    private SavedAiJson? GetSelectedSavedJson()
    {
        var index = _lstSavedJson.SelectedIndex;
        if (index < 0 || index >= _currentSavedJsonHistory.Count) return null;
        return _currentSavedJsonHistory[index];
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
    /// 为 shell 命令构造进程启动信息。
    /// </summary>
    private static System.Diagnostics.ProcessStartInfo BuildShellProcessStartInfo(string shell, string commandText, string workingDirectory)
    {
        var shellName = (shell ?? "powershell").Trim().ToLowerInvariant();
        
        // 自动探测并修正：如果明确包含 cmd 特有的语法，强制切换为 cmd 模式
        if (shellName == "powershell" && (commandText.Contains("&&") || commandText.Contains("cd /d ")))
        {
            shellName = "cmd";
        }

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
    /// 获取当前活动的文本控件，供状态栏显示光标位置。
    /// </summary>
    private TextBoxBase? GetActiveTextControl()
    {
        if (_editorTabs.SelectedTab?.Controls.OfType<RichTextBox>().FirstOrDefault() is RichTextBox editor &&
            (_editorTabs.ContainsFocus || editor.ContainsFocus))
            return editor;

        if (_txtPlan.ContainsFocus)
            return _txtPlan;
            
        if (_txtAi.ContainsFocus)
            return _txtAi;
            
        return _txtPlan;
    }





















    // ============== C++ 编译与运行 ==============



    /// <summary>
    /// 执行外部进程，并异步同时收集标准输出和标准错误，避免 ReadToEnd 死锁。
    /// </summary>
    private async System.Threading.Tasks.Task<(int ExitCode, string StdOut, string StdErr, bool TimedOut, bool StoppedByUser)> RunProcessCaptureAsync(
        System.Diagnostics.ProcessStartInfo startInfo,
        int timeoutMs,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        string? displayName = null)
    {
        using var proc = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdOut.AppendLine(e.Data);
                onStdOutLine?.Invoke(e.Data);
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                var cleanData = e.Data;
                if (cleanData.StartsWith("#< CLIXML") || cleanData.StartsWith("<Objs Version=")) return;
                if (cleanData.Contains("_x000D__x000A_"))
                {
                    cleanData = System.Text.RegularExpressions.Regex.Replace(cleanData, "<.*?>", "");
                    cleanData = cleanData.Replace("_x000D__x000A_", "").Replace("&amp;", "&");
                }
                if (!string.IsNullOrWhiteSpace(cleanData))
                {
                    stdErr.AppendLine(cleanData);
                    onStdErrLine?.Invoke(cleanData);
                }
            }
        };

        if (!proc.Start())
            throw new InvalidOperationException($"无法启动进程：{startInfo.FileName}");

        var actualDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? $"{startInfo.FileName} {startInfo.Arguments}".Trim()
            : displayName;
        if (!TryAttachActiveRunProcess(proc, actualDisplayName))
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // 忽略并发清理异常
            }

            throw new InvalidOperationException("当前已有命令正在执行，请先停止或等待完成。");
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        var timedOut = false;
        var stoppedByUser = false;
        var exitCode = -1;

        try
        {
            var waitTask = proc.WaitForExitAsync();
            var timeoutTask = System.Threading.Tasks.Task.Delay(timeoutMs);
            var finishedTask = await System.Threading.Tasks.Task.WhenAny(waitTask, timeoutTask);

            if (finishedTask == timeoutTask)
            {
                timedOut = true;
                try
                {
                    if (!proc.HasExited)
                        proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    // 忽略超时杀进程时的清理异常
                }

                return (-1, stdOut.ToString(), stdErr.ToString(), true, false);
            }

            await waitTask;
            lock (_activeRunProcessSync)
            {
                stoppedByUser = ReferenceEquals(_activeRunProcess, proc) && _activeRunStopRequested;
            }
            exitCode = proc.ExitCode;
            return (proc.ExitCode, stdOut.ToString(), stdErr.ToString(), false, stoppedByUser);
        }
        finally
        {
            if (proc.HasExited && exitCode == -1)
            {
                exitCode = proc.ExitCode;
            }
            if (!timedOut)
            {
                lock (_activeRunProcessSync)
                {
                    if (ReferenceEquals(_activeRunProcess, proc))
                    {
                        stoppedByUser = _activeRunStopRequested;
                    }
                }
            }

            DetachActiveRunProcess(proc, timedOut, stoppedByUser, exitCode);
        }
    }





    /// <summary>
    /// 只执行当前项目的编译步骤，结果写到下方命令输出区。
    /// </summary>
    private async Task<bool> BuildCppCodeAsync(bool clearOutput = true)
    {
        if (!PrepareRunOutputPanel("准备编译 C++ 代码...", clearOutput)) return false;
        if (!TryGetCppBuildContext(out var exePath, out var filesArgs)) return false;

        try
        {
            return await BuildCppProjectCoreAsync(exePath, filesArgs);
        }
        catch (Exception ex)
        {
            if (string.Equals(ex.Message, "当前已有命令正在执行，请先停止或等待完成。", StringComparison.Ordinal))
            {
                AppendRunOutput("⚠ 当前已有命令正在执行，请先停止或等待完成。\n", Warning);
                ShowTransientStatus("● 当前已有命令正在执行", Warning);
                return false;
            }
            AppendRunOutput($"❌ 编译阶段发生异常：{ex.Message}\n");
            return false;
        }
    }

    /// <summary>
    /// 只执行当前项目的运行步骤，优先使用默认运行命令。
    /// </summary>
    private async Task<bool> RunBuiltProgramAsync(bool clearOutput = true)
    {
        if (!PrepareRunOutputPanel("准备运行 C++ 程序...", clearOutput)) return false;

        try
        {
            return await RunCppProjectCoreAsync(Path.Combine(_projectRoot!, "app_run.exe"));
        }
        catch (Exception ex)
        {
            if (string.Equals(ex.Message, "当前已有命令正在执行，请先停止或等待完成。", StringComparison.Ordinal))
            {
                AppendRunOutput("⚠ 当前已有命令正在执行，请先停止或等待完成。\n", Warning);
                ShowTransientStatus("● 当前已有命令正在执行", Warning);
                return false;
            }
            AppendRunOutput($"❌ 运行阶段发生异常：{ex.Message}\n");
            return false;
        }
    }



    /// <summary>
    /// 执行编译核心流程，优先使用用户设定的默认编译命令。
    /// </summary>
    private async Task<bool> BuildCppProjectCoreAsync(string exePath, string filesArgs)
    {
        var savedBuildCommand = _settings.GetDefaultBuildCommand(_projectRoot!);
        if (savedBuildCommand != null)
        {
            AppendRunOutput("📌 已使用右侧置顶的默认编译命令。\n");
            return await ExecuteSavedCommandToRunOutputAsync(savedBuildCommand, "编译", 60_000);
        }

        // 自动探测 SConstruct
        bool useScons = File.Exists(Path.Combine(_projectRoot!, "SConstruct"));

        System.Diagnostics.ProcessStartInfo buildInfo;
        if (useScons)
        {
            AppendRunOutput("> scons -Q\n");
            buildInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "scons",
                Arguments = "-Q",
                WorkingDirectory = _projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            var compiler = DetectCompiler();
            AppendRunOutput($"> {compiler} {filesArgs} -o {Path.GetFileName(exePath)}\n");

            buildInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = compiler,
                Arguments = $"{filesArgs} -o \"{Path.GetFileName(exePath)}\"",
                WorkingDirectory = _projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        var buildResult = await RunProcessCaptureAsync(
            buildInfo,
            timeoutMs: 60_000,
            onStdOutLine: line => AppendRunOutput(line + "\n"),
            onStdErrLine: line => AppendRunOutput(line + "\n", Color.IndianRed),
            displayName: useScons ? "编译: scons" : $"编译: {buildInfo.FileName}");

        if (buildResult.TimedOut)
        {
            AppendRunOutput("❌ 编译超时：60 秒内未完成，已自动终止编译进程。\n");
            return false;
        }

        if (buildResult.StoppedByUser)
        {
            AppendRunOutput("⏹ 编译已手动停止。\n", Warning);
            return false;
        }

        if (buildResult.ExitCode != 0)
        {
            AppendRunOutput($"❌ 编译失败，退出码：{buildResult.ExitCode}\n");
            return false;
        }

        if (useScons)
        {
            AppendRunOutput("✅ scons 编译成功\n");
        }
        else
        {
            AppendRunOutput($"✅ 编译成功，输出：{exePath}\n");
        }
        return true;
    }

    /// <summary>
    /// 执行运行核心流程，优先使用用户设定的默认运行命令。
    /// </summary>
    private async Task<bool> RunCppProjectCoreAsync(string exePath)
    {
        var savedRunCommand = _settings.GetDefaultRunCommand(_projectRoot!);
        if (savedRunCommand != null)
        {
            AppendRunOutput("📌 已使用右侧置顶的默认运行命令。\n");
            return await ExecuteSavedCommandToRunOutputAsync(savedRunCommand, "运行", 30_000);
        }

        if (!File.Exists(exePath))
        {
            AppendRunOutput("⚠ 当前未设置默认运行命令，且未找到 app_run.exe。请先点击“编译”。\n");
            return false;
        }

        AppendRunOutput($"▶ 开始运行：{Path.GetFileName(exePath)}\n" + new string('-', 40) + "\n");

        var runInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var runResult = await RunProcessCaptureAsync(
            runInfo,
            timeoutMs: 30_000,
            onStdOutLine: line => AppendRunOutput(line + "\n"),
            onStdErrLine: line => AppendRunOutput(line + "\n", Color.IndianRed),
            displayName: $"运行: {Path.GetFileName(exePath)}");

        if (runResult.TimedOut)
        {
            AppendRunOutput("\n" + new string('-', 40) + "\n⚠ 运行超时：30 秒内未退出，已自动终止程序。\n");
            return false;
        }

        if (runResult.StoppedByUser)
        {
            AppendRunOutput("\n" + new string('-', 40) + "\n⏹ 程序已手动停止。\n", Warning);
            return false;
        }

        AppendRunOutput("\n" + new string('-', 40) + $"\n✅ 运行结束，退出码：{runResult.ExitCode}\n");
        return runResult.ExitCode == 0;
    }



    // ============== 事件处理 ==============







    private static readonly string[] _skipDirs = new[]
    {
        ".git", ".vs", "node_modules", "target", "build", "dist", ".idea", ".vscode",
        "__pycache__", "venv", ".venv"
    };















}
