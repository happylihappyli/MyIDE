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
    private readonly TableLayoutPanel _savedJsonPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = BgPanel, Padding = new Padding(6) };
    private readonly TableLayoutPanel _savedCommandsPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = BgPanel, Padding = new Padding(6) };
    private readonly Label _lblSavedJsonHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private readonly Label _lblSavedCommandsHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private readonly Label _lblLogHeader = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = BgHeader, ForeColor = FgText, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private readonly System.Windows.Forms.Timer _aiJsonSidebarSyncTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _planSaveTimer = new() { Interval = 700 };
    private readonly ContextMenuStrip _treeMenu = new();
    private List<SavedAiJson> _currentSavedJsonHistory = new();
    private List<SavedAiCommand> _currentSavedCommands = new();
    private bool _isSavedJsonCollapsed;
    private bool _isSavedCommandsCollapsed;
    private bool _isLogCollapsed;
    private string _lastSidebarSyncedAiJson = "";

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
        _isSavedJsonCollapsed = _settings.IsSavedJsonCollapsed;
        _isSavedCommandsCollapsed = _settings.IsSavedCommandsCollapsed;
        _isLogCollapsed = _settings.IsLogCollapsed;

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
        InitializeSavedJsonContextMenu();
        InitializeSavedCommandContextMenu();
        UpdateAiJsonBufferStatus();

        _txtPlan.Click += UpdateCursorPos;
        _txtPlan.KeyUp += UpdateCursorPos;
        _txtPlan.TextChanged += (_, _) => SchedulePlanTextSave();
        _txtAi.TextChanged += (_, _) => UpdateAiJsonBufferStatus();
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
        _lstSavedJson.KeyDown += SavedJsonList_KeyDown;
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
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

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
        var btnMyChrome = MakeToolButton("🌐 启动 MyChrome", async (_, _) => await LaunchMyChromeAsync());
        var btnNewSession = MakeToolButton("🆕 新 Session", (_, _) => StartNewPromptSession());
        var btnCloseEditor = MakeToolButton("✖ 关闭代码页", (_, _) => CloseCurrentEditorTab());
        var btnGen = MakeToolButton("✨ 生成提示词", BtnGen_Click);
        var btnCopy = MakeToolButton("📋 复制提示词", (_, _) => CopyPromptToClipboard());
        var btnPasteJson = MakeToolButton("📥 粘贴 JSON", (_, _) => HandleAiJsonDialogAction(ShowAiJsonInputDialog()));
        var btnPreview = MakeToolButton("🔍 预览 Diff", BtnPreview_Click);
        var btnApply = MakeToolButton("✅ 应用", BtnApply_Click);
        var btnUndo = MakeToolButton("↩ 撤销", (_, _) => DoUndo());
        var btnClearLog = MakeToolButton("🧹 清空日志", (_, _) => ClearLogPanel());

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            btnOpen, btnRefresh, btnMyChrome, btnNewSession, btnCloseEditor, new ToolStripSeparator(),
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

    /// <summary>
    /// 新开一个 AI Session，让下一次生成提示词时重新包含完整 JSON 协议说明。
    /// </summary>
    private void StartNewPromptSession()
    {
        _settings.IncludePromptProtocolOnNextPrompt = true;
        _settings.Save();
        _lastGeneratedPrompt = "";
        _lblStatus.Text = "● 已新建 Session";
        _lblStatus.ForeColor = Accent;
        Log("✔ 已标记为新 Session；下一次生成提示词将重新附带完整 JSON 协议说明。");
    }

    /// <summary>
    /// 延迟保存“我的计划”文本，避免每次按键都落盘设置文件。
    /// </summary>
    private void SchedulePlanTextSave()
    {
        _planSaveTimer.Stop();
        _planSaveTimer.Start();
    }

    /// <summary>
    /// 把当前计划内容写入设置，保证下次打开时自动恢复。
    /// </summary>
    private void SaveCurrentPlanText()
    {
        var planText = _txtPlan.Text ?? "";
        if (string.Equals(_settings.LastPlanText, planText, StringComparison.Ordinal)) return;

        _settings.LastPlanText = planText;
        _settings.Save();
    }

    /// <summary>
    /// 打开 AI JSON 粘贴对话框，支持直接保存、预览或应用。
    /// </summary>
    private AiJsonDialogAction ShowAiJsonInputDialog()
    {
        AiJsonDialogAction action = AiJsonDialogAction.None;
        var recentHistory = GetRecentSavedJsonHistory(3);
        using var dlg = new Form
        {
            Text = "粘贴 AI 返回 JSON",
            Width = 1180,
            Height = 760,
            MinimumSize = new Size(980, 720),
            StartPosition = FormStartPosition.CenterParent,
            KeyPreview = true,
            BackColor = BgDark,
            ForeColor = FgText
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BgDark
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        var tipLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = FgMuted,
            BackColor = BgDark,
            Padding = new Padding(8, 0, 0, 0),
            Text = "左侧可粘贴或直接从 MyChrome 读取最新回复；右侧可查看解析摘要、commands 快捷提示，并从最近 3 条历史一键带入。快捷键：Ctrl+Enter 应用，Ctrl+Shift+Enter 预览，Ctrl+M 读取，Ctrl+Delete 清空。"
        };
        layout.Controls.Add(tipLabel, 0, 0);

        var contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = BgHeader
        };
        var lastDialogSplitterDistance = _settings.AiJsonDialogSplitterDistance > 0
            ? _settings.AiJsonDialogSplitterDistance
            : 700;
        void updateDialogSplitterDistance(int splitterDistance, bool saveImmediately)
        {
            if (splitterDistance <= 0) return;

            lastDialogSplitterDistance = splitterDistance;
            _settings.AiJsonDialogSplitterDistance = splitterDistance;
            if (saveImmediately)
            {
                _settings.Save();
            }
        }

        void saveDialogSplitterDistance()
        {
            updateDialogSplitterDistance(lastDialogSplitterDistance, saveImmediately: true);
        }

        contentSplit.SplitterMoving += (_, e) => updateDialogSplitterDistance(e.SplitX, saveImmediately: false);
        contentSplit.SplitterMoved += (_, _) => updateDialogSplitterDistance(contentSplit.SplitterDistance, saveImmediately: true);

        var tb = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = _chkAiWrap.Checked ? ScrollBars.Vertical : ScrollBars.Both,
            WordWrap = _chkAiWrap.Checked,
            Font = new Font("Cascadia Mono", 10),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = BgDark,
            ForeColor = FgText,
            Text = _txtAi.Text
        };
        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BgDark
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var quickActionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BgDark,
            Padding = new Padding(0, 4, 0, 0)
        };
        contentSplit.Panel1.Padding = new Padding(0, 0, 8, 0);
        contentSplit.Panel1.Controls.Add(leftPanel);

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = BgDark
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var commandsTipBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Font = new Font("Cascadia Mono", 9),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = BgDark,
            ForeColor = FgMuted,
            Text =
                "快捷提示：\r\n" +
                "1. 只想执行 commands 也可以，保留 \"changes\": [] 即可。\r\n" +
                "2. 点击“保存并应用”后，会直接进入命令执行窗口，不再先弹 Diff。\r\n" +
                "3. 这样更适合把编译、运行、测试命令直接交给 MyIDE 执行。\r\n" +
                "4. Ctrl+L 可一键插入 commands 模板，Ctrl+Shift+F 可格式化 JSON。"
        };

        var summaryBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Font = new Font("Cascadia Mono", 9),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = BgDark,
            ForeColor = FgText
        };
        var recentLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = FgMuted,
            BackColor = BgDark,
            Text = "最近 3 条 JSON 历史"
        };
        var recentList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = BgDark,
            ForeColor = FgText,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Cascadia Mono", 9),
            HorizontalScrollbar = true
        };
        var recentDetailBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Font = new Font("Cascadia Mono", 9),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = BgDark,
            ForeColor = FgText
        };
        var recentButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BgDark,
            Padding = new Padding(0, 6, 0, 0)
        };
        var btnUseRecent = makeButton("带入所选历史", Color.FromArgb(60, 60, 60));
        btnUseRecent.Width = 136;
        var btnUseLatest = makeButton("带入最新一条", Accent);
        btnUseLatest.Width = 136;
        var btnInsertCommandsTemplate = makeButton("仅 commands 模板", Color.FromArgb(79, 97, 40));
        btnInsertCommandsTemplate.Width = 136;
        var btnFormatJson = makeButton("格式化 JSON", Color.FromArgb(60, 60, 60));
        btnFormatJson.Width = 120;
        var btnReadFromMyChrome = makeButton("从 MyChrome 读取", Color.FromArgb(0, 122, 204));
        btnReadFromMyChrome.Width = 148;
        var btnClearEditor = makeButton("清空左侧", Color.FromArgb(90, 45, 45));
        btnClearEditor.Width = 96;

        SavedAiJson? getSelectedRecent()
        {
            var index = recentList.SelectedIndex;
            if (index < 0 || index >= recentHistory.Count) return null;
            return recentHistory[index];
        }

        void updateRecentDetail()
        {
            var item = getSelectedRecent();
            if (item == null)
            {
                recentDetailBox.Text = string.IsNullOrWhiteSpace(_projectRoot)
                    ? "当前还没有打开项目，无法读取 JSON 历史。"
                    : "当前项目还没有可复用的 JSON 历史。";
                return;
            }

            recentDetailBox.Text =
                $"任务：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}\r\n" +
                $"时间：{item.UpdatedAt:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"修改：{item.ChangeCount} 个文件\r\n" +
                $"命令：{item.CommandCount} 条\r\n\r\n" +
                item.JsonText;
        }

        void importRecent(SavedAiJson? item)
        {
            if (item == null)
            {
                Warn("当前没有可带入的 JSON 历史");
                return;
            }

            tb.Text = item.JsonText;
            tb.SelectionStart = tb.TextLength;
            tb.SelectionLength = 0;
            tb.Focus();
            Log($"✔ 已从最近历史带入 JSON：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
        }

        void insertCommandsTemplate()
        {
            tb.Text = BuildCommandsOnlyJsonTemplate();
            tb.SelectionStart = tb.TextLength;
            tb.SelectionLength = 0;
            tb.Focus();
            Log("✔ 已插入仅 commands 的 JSON 模板。");
        }

        void formatJsonInEditor()
        {
            if (TryFormatAiJsonText(tb.Text, out var formatted, out var errorMessage))
            {
                tb.Text = formatted;
                tb.SelectionStart = tb.TextLength;
                tb.SelectionLength = 0;
                tb.Focus();
                Log("✔ 已格式化当前 JSON。");
                return;
            }

            Warn("格式化 JSON 失败：" + errorMessage);
        }

        async Task readJsonFromMyChromeAsync()
        {
            btnReadFromMyChrome.Enabled = false;
            btnReadFromMyChrome.Text = "读取中...";
            try
            {
                var result = await TryReadLatestReplyFromAiBrowserAsync();
                if (!result.Ok)
                {
                    Warn("从 MyChrome 读取失败：" + (string.IsNullOrWhiteSpace(result.Message) ? result.Reason : result.Message));
                    if (!string.IsNullOrWhiteSpace(result.DebugInfo))
                    {
                        Log($"· MyChrome 调试信息：{result.DebugInfo}");
                    }
                    return;
                }

                var replyText = result.ReplyText ?? "";
                if (string.IsNullOrWhiteSpace(replyText))
                {
                    Warn("MyChrome 已返回结果，但回复内容为空。");
                    return;
                }

                #region debug-point C:reply-from-mychrome
                await ReportDebugEventAsync("C", "MainForm.readJsonFromMyChromeAsync:replyText", "received text from MyChrome", new
                {
                    copyMethod = result.CopyMethod,
                    reply = BuildDebugTextSnapshot(replyText),
                    source = result.Source,
                    url = result.Url
                });
                #endregion

                var importedText = replyText;
                var normalizedByRepair = false;
                try
                {
                    var extracted = ExtractJsonBody(replyText);
                    importedText = NormalizeAiJsonBody(replyText);
                    normalizedByRepair = !string.Equals(extracted, importedText, StringComparison.Ordinal);
                }
                catch
                {
                    importedText = replyText;
                }

                #region debug-point C:imported-text
                await ReportDebugEventAsync("C", "MainForm.readJsonFromMyChromeAsync:importedText", "text after ExtractJsonBody", new
                {
                    changed = !string.Equals(replyText, importedText, StringComparison.Ordinal),
                    normalizedByRepair,
                    copyMethod = result.CopyMethod,
                    reply = BuildDebugTextSnapshot(replyText),
                    imported = BuildDebugTextSnapshot(importedText)
                });
                #endregion

                tb.Text = importedText;
                tb.SelectionStart = tb.TextLength;
                tb.SelectionLength = 0;
                tb.Focus();
                Log($"✔ 已从 MyChrome 读取最新回复：{BuildShortPreview(result.Source)} {BuildShortPreview(result.Url, 48)}");
            }
            finally
            {
                btnReadFromMyChrome.Enabled = true;
                btnReadFromMyChrome.Text = "从 MyChrome 读取";
            }
        }

        void clearEditorText()
        {
            tb.Clear();
            tb.Focus();
            Log("✔ 已清空左侧 JSON 编辑区。");
        }

        foreach (var item in recentHistory)
        {
            recentList.Items.Add(BuildRecentSavedJsonDialogListText(item));
        }

        recentList.SelectedIndexChanged += (_, _) => updateRecentDetail();
        recentList.DoubleClick += (_, _) => importRecent(getSelectedRecent());
        btnUseRecent.Click += (_, _) => importRecent(getSelectedRecent());
        btnUseLatest.Click += (_, _) => importRecent(recentHistory.FirstOrDefault());
        btnInsertCommandsTemplate.Click += (_, _) => insertCommandsTemplate();
        btnFormatJson.Click += (_, _) => formatJsonInEditor();
        btnReadFromMyChrome.Click += async (_, _) => await readJsonFromMyChromeAsync();
        btnClearEditor.Click += (_, _) => clearEditorText();

        if (recentList.Items.Count > 0)
        {
            recentList.SelectedIndex = 0;
        }
        else
        {
            btnUseRecent.Enabled = false;
            btnUseLatest.Enabled = false;
            updateRecentDetail();
        }

        quickActionPanel.Controls.Add(btnInsertCommandsTemplate);
        quickActionPanel.Controls.Add(btnFormatJson);
        quickActionPanel.Controls.Add(btnReadFromMyChrome);
        quickActionPanel.Controls.Add(btnClearEditor);
        leftPanel.Controls.Add(quickActionPanel, 0, 0);
        leftPanel.Controls.Add(tb, 0, 1);

        // 移除带入历史按钮
        // recentButtonPanel.Controls.Add(btnUseLatest);
        // recentButtonPanel.Controls.Add(btnUseRecent);

        contentSplit.Panel2.Padding = new Padding(8, 0, 0, 0);
        rightPanel.Controls.Add(commandsTipBox, 0, 0);
        rightPanel.Controls.Add(summaryBox, 0, 1);
        rightPanel.Controls.Add(recentLabel, 0, 2);
        rightPanel.Controls.Add(recentList, 0, 3);
        rightPanel.Controls.Add(recentDetailBox, 0, 4);
        rightPanel.Controls.Add(recentButtonPanel, 0, 5);
        contentSplit.Panel2.Controls.Add(rightPanel);
        layout.Controls.Add(contentSplit, 0, 1);

        void refreshSummary()
        {
            summaryBox.Text = BuildAiJsonSummaryText(tb.Text, showRawHiddenHint: false);
        }

        tb.TextChanged += (_, _) => refreshSummary();
        refreshSummary();

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 8, 8, 8),
            BackColor = BgDark
        };
        Button makeButton(string text, Color backColor)
        {
            var button = new Button
            {
                Text = text,
                Width = 120,
                Height = 32,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        void saveToBuffer()
        {
            _txtAi.Text = tb.Text;
            TrySaveCurrentAiJsonToSidebar();
            UpdateAiJsonBufferStatus();
        }

        var btnCancel = makeButton("取消", Color.FromArgb(60, 60, 60));
        btnCancel.Click += (_, _) =>
        {
            action = AiJsonDialogAction.None;
            dlg.DialogResult = DialogResult.Cancel;
            dlg.Close();
        };

        var btnSave = makeButton("仅保存", Accent);
        btnSave.Click += (_, _) =>
        {
            updateDialogSplitterDistance(contentSplit.SplitterDistance, saveImmediately: false);
            saveToBuffer();
            action = AiJsonDialogAction.Save;
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
        };

        var btnPreview = makeButton("保存并预览", Color.FromArgb(79, 97, 40));
        btnPreview.Click += (_, _) =>
        {
            updateDialogSplitterDistance(contentSplit.SplitterDistance, saveImmediately: false);
            saveToBuffer();
            action = AiJsonDialogAction.Preview;
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
        };

        var btnApply = makeButton("保存并应用", Color.FromArgb(0, 153, 102));
        btnApply.Click += (_, _) =>
        {
            updateDialogSplitterDistance(contentSplit.SplitterDistance, saveImmediately: false);
            saveToBuffer();
            action = AiJsonDialogAction.Apply;
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
        };

        dlg.KeyDown += (_, e) =>
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                btnPreview.PerformClick();
                return;
            }

            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                btnApply.PerformClick();
                return;
            }

            if (e.Control && e.KeyCode == Keys.S)
            {
                e.SuppressKeyPress = true;
                btnSave.PerformClick();
                return;
            }

            if (e.Control && e.KeyCode == Keys.L)
            {
                e.SuppressKeyPress = true;
                btnInsertCommandsTemplate.PerformClick();
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.F)
            {
                e.SuppressKeyPress = true;
                btnFormatJson.PerformClick();
                return;
            }

            if (e.Control && e.KeyCode == Keys.M)
            {
                e.SuppressKeyPress = true;
                btnReadFromMyChrome.PerformClick();
                return;
            }

            if (e.Control && e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                btnClearEditor.PerformClick();
            }
        };

        bottom.Controls.Add(btnCancel);
        bottom.Controls.Add(btnApply);
        bottom.Controls.Add(btnPreview);
        bottom.Controls.Add(btnSave);

        layout.Controls.Add(bottom, 0, 2);
        dlg.Controls.Add(layout);
        dlg.Shown += (_, _) =>
        {
            ConfigureDialogSplitterLayout(
                contentSplit,
                panel1MinSize: 520,
                panel2MinSize: 300,
                preferredDistance: lastDialogSplitterDistance);

            dlg.BeginInvoke(new Action(() =>
            {
                if (!dlg.IsDisposed)
                {
                    ApplySafeSplitterDistance(
                        contentSplit,
                        lastDialogSplitterDistance);
                }
            }));
        };
        dlg.FormClosing += (_, _) =>
        {
            saveDialogSplitterDistance();
        };
        dlg.ShowDialog(this);
        return action;
    }

    /// <summary>
    /// 在对话框真正显示后一次性设置最小尺寸和分隔位置，避免初始化阶段抛出 SplitterDistance 异常。
    /// </summary>
    private static void ConfigureDialogSplitterLayout(SplitContainer splitContainer, int panel1MinSize, int panel2MinSize, int preferredDistance)
    {
        if (splitContainer.IsDisposed) return;

        splitContainer.Panel1MinSize = panel1MinSize;
        splitContainer.Panel2MinSize = panel2MinSize;
        ApplySafeSplitterDistance(splitContainer, preferredDistance);
    }

    /// <summary>
    /// 在控件尺寸稳定后安全设置分栏位置，避免初始化阶段触发非法 SplitterDistance。
    /// </summary>
    private static void ApplySafeSplitterDistance(SplitContainer splitContainer, int preferredDistance)
    {
        if (splitContainer.IsDisposed) return;

        var availableWidth = splitContainer.ClientSize.Width - splitContainer.SplitterWidth;
        if (availableWidth <= 0) return;

        var minDistance = splitContainer.Panel1MinSize;
        var maxDistance = availableWidth - splitContainer.Panel2MinSize;
        if (maxDistance < minDistance) return;

        splitContainer.SplitterDistance = Math.Min(Math.Max(preferredDistance, minDistance), maxDistance);
    }

    /// <summary>
    /// 获取当前项目最近更新的几条 AI JSON 历史，供弹窗右侧快速带入。
    /// </summary>
    private List<SavedAiJson> GetRecentSavedJsonHistory(int limit)
    {
        if (string.IsNullOrWhiteSpace(_projectRoot) || limit <= 0) return new List<SavedAiJson>();

        return _settings.SavedJsonHistory
            .Where(item => string.Equals(item.ProjectRoot, _projectRoot, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// 生成弹窗内“最近 JSON 历史”列表项文本，让时间和命令数量更直观。
    /// </summary>
    private static string BuildRecentSavedJsonDialogListText(SavedAiJson item)
    {
        var title = string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task;
        if (title.Length > 18) title = title[..18] + "...";
        return $"{item.UpdatedAt:MM-dd HH:mm} [C{item.ChangeCount}/M{item.CommandCount}] {title}";
    }

    /// <summary>
    /// 生成一个仅执行 commands 的 JSON 模板，便于快速走命令流。
    /// </summary>
    private static string BuildCommandsOnlyJsonTemplate()
    {
        var template = new ChangePlan
        {
            Task = "执行命令",
            Changes = new List<FileChange>(),
            Commands = new List<AiCommand>
            {
                new()
                {
                    Name = "编译项目",
                    Reason = "验证当前修改是否可编译",
                    Command = "dotnet build -c Release",
                    Shell = "powershell",
                    WorkingDirectory = ".",
                    Optional = false
                }
            }
        };

        return JsonSerializer.Serialize(template, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    /// <summary>
    /// 将 AI JSON 规范化并格式化为缩进文本，容忍 Markdown 代码块包裹。
    /// </summary>
    private static bool TryFormatAiJsonText(string rawText, out string formattedText, out string errorMessage)
    {
        formattedText = "";
        errorMessage = "";

        if (string.IsNullOrWhiteSpace(rawText))
        {
            errorMessage = "当前没有可格式化的内容";
            return false;
        }

        try
        {
            var jsonBody = NormalizeAiJsonBody(rawText);
            using var doc = JsonDocument.Parse(jsonBody, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            formattedText = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 从 AI 返回文本中提取 JSON 主体，兼容 ```json 包裹和前后说明文字。
    /// </summary>
    private static string ExtractJsonBody(string rawText)
    {
        var text = (rawText ?? "").Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd > 0)
            {
                text = text[(firstLineEnd + 1)..];
            }

            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0)
            {
                text = text[..lastFence];
            }
        }

        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            text = text.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return text.Trim();
    }

    /// <summary>
    /// 统一规范化 AI JSON 正文，优先保留原始文本，必要时自动修复 DOM 提取造成的字符串转义损坏。
    /// </summary>
    private static string NormalizeAiJsonBody(string rawText)
    {
        var jsonBody = ExtractJsonBody(rawText);
        if (CanParseJson(jsonBody)) return jsonBody;

        return TryRepairRenderedAiJson(jsonBody, out var repaired) ? repaired : jsonBody;
    }

    /// <summary>
    /// 判断当前文本是否已经是可解析的 JSON，避免重复修复。
    /// </summary>
    private static bool CanParseJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 修复 DOM 渲染文本中被展开的 JSON 字符串，主要补回内部引号、换行和非法反斜杠的转义。
    /// </summary>
    private static bool TryRepairRenderedAiJson(string text, out string repairedText)
    {
        repairedText = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(repairedText)) return false;

        var sb = new StringBuilder(repairedText.Length + 64);
        var inString = false;
        var afterBackslash = false;

        for (var i = 0; i < repairedText.Length; i++)
        {
            var ch = repairedText[i];

            if (!inString)
            {
                sb.Append(ch);
                if (ch == '"')
                {
                    inString = true;
                    afterBackslash = false;
                }
                continue;
            }

            if (afterBackslash)
            {
                sb.Append(ch);
                afterBackslash = false;
                continue;
            }

            if (ch == '\\')
            {
                var next = i + 1 < repairedText.Length ? repairedText[i + 1] : '\0';
                if (next != '\0' && "\"\\/bfnrtu".IndexOf(next) >= 0)
                {
                    sb.Append(ch);
                    afterBackslash = true;
                }
                else
                {
                    sb.Append("\\\\");
                }
                continue;
            }

            if (ch == '"')
            {
                var closesString = IsLikelyJsonStringTerminator(repairedText, i);
                if (closesString)
                {
                    sb.Append('"');
                    inString = false;
                }
                else
                {
                    sb.Append("\\\"");
                }
                continue;
            }

            if (ch == '\r')
            {
                if (i + 1 < repairedText.Length && repairedText[i + 1] == '\n')
                {
                    sb.Append("\\r\\n");
                    i++;
                }
                else
                {
                    sb.Append("\\r");
                }
                continue;
            }

            if (ch == '\n')
            {
                sb.Append("\\n");
                continue;
            }

            sb.Append(ch);
        }

        var candidate = sb.ToString();
        if (!CanParseJson(candidate))
        {
            return TryRepairRenderedAiJsonByPropertyLines(repairedText, out repairedText);
        }

        repairedText = candidate;
        return true;
    }

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

    /// <summary>
    /// 判断字符串中的双引号在当前上下文里是否更像 JSON 字符串结束符，而不是源码内部引号。
    /// </summary>
    private static bool IsLikelyJsonStringTerminator(string text, int quoteIndex)
    {
        var j = quoteIndex + 1;
        while (j < text.Length && char.IsWhiteSpace(text[j]))
        {
            j++;
        }

        if (j >= text.Length) return true;

        var next = text[j];
        if (next == '}' || next == ']')
        {
            return true;
        }

        if (next != ',')
        {
            return false;
        }

        j++;
        while (j < text.Length && char.IsWhiteSpace(text[j]))
        {
            j++;
        }

        if (j >= text.Length) return true;

        next = text[j];
        return next == '"' || next == '}' || next == ']' || next == '{' || next == '[';
    }

    #region debug-point C:report-helper
    /// <summary>
    /// 向调试服务器记录从 MyChrome 读取和导入处理前后的文本状态。
    /// </summary>
    private static async Task ReportDebugEventAsync(string hypothesisId, string location, string message, object? data = null, string runId = "post-fix")
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                sessionId = "deepseek-copy-mismatch",
                runId,
                hypothesisId,
                location,
                msg = "[DEBUG] " + message,
                data,
                ts = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await DebugHttpClient.PostAsync("http://127.0.0.1:7777/event", content);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 生成供调试对比使用的文本摘要，避免把整段 JSON 原文直接写入日志。
    /// </summary>
    private static object BuildDebugTextSnapshot(string? text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return new
        {
            length = normalized.Length,
            preview = normalized.Length <= 160 ? normalized : normalized[..160] + "...",
            hash = normalized.Length == 0 ? "empty:0" : $"{normalized.GetHashCode(StringComparison.Ordinal)}:{normalized.Length}"
        };
    }
    #endregion


    /// <summary>
    /// 清空当前暂存的 AI JSON，并刷新下方摘要状态。
    /// </summary>
    private void ClearCurrentAiJson()
    {
        _txtAi.Clear();
        UpdateAiJsonBufferStatus();
        Log("✔ 已清空当前暂存的 AI JSON。");
    }

    /// <summary>
    /// 构建 AI JSON 的摘要文本，供主页和弹窗右侧统一复用。
    /// </summary>
    private string BuildAiJsonSummaryText(string jsonText, bool showRawHiddenHint = true)
    {
        var normalized = (jsonText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "当前没有暂存的 AI JSON。\r\n\r\n点击“粘贴 / 编辑 JSON”按钮后，会弹出对话框供你粘贴内容。";
        }

        try
        {
            if (_projectRoot != null)
            {
                var applier = new ChangeApplier(_projectRoot);
                var plan = applier.ParseJson(normalized);
                var filePreview = plan.Changes.Count == 0
                    ? "无"
                    : string.Join("、", plan.Changes.Take(4).Select(c => c.File));
                var commandPreview = plan.Commands.Count == 0
                    ? "无"
                    : string.Join("、", plan.Commands.Take(4).Select(c => string.IsNullOrWhiteSpace(c.Name) ? c.Command : c.Name));

                var summary =
                    $"解析状态：可用\r\n" +
                    $"任务：{(string.IsNullOrWhiteSpace(plan.Task) ? "未命名任务" : plan.Task)}\r\n" +
                    $"修改：{plan.Changes.Count} 个文件\r\n" +
                    $"命令：{plan.Commands.Count} 条\r\n" +
                    $"修改文件预览：{filePreview}\r\n" +
                    $"命令预览：{commandPreview}\r\n" +
                    $"长度：{normalized.Length} 字符";
                if (showRawHiddenHint)
                {
                    summary += "\r\n\r\n原始 JSON 已隐藏，不再直接显示在下方页签区域。";
                }
                if (plan.Changes.Count == 0 && plan.Commands.Count > 0)
                {
                    summary += "\r\n\r\n快捷提示：当前是仅 commands JSON，点击“保存并应用”会直接进入命令执行窗口。";
                }

                return summary;
            }
        }
        catch (Exception ex)
        {
            return
                $"解析状态：暂不可用\r\n" +
                $"原因：{ex.Message}\r\n\r\n" +
                $"长度：{normalized.Length} 字符\r\n" +
                "可以继续编辑 JSON，修正后这里会自动刷新摘要。";
        }

        return
            $"当前已暂存 AI JSON\r\n长度：{normalized.Length} 字符" +
            (showRawHiddenHint ? "\r\n\r\n原始 JSON 已隐藏，不再直接显示在下方页签区域。" : "");
    }

    /// <summary>
    /// 用摘要而不是原始全文显示当前暂存的 AI JSON 状态。
    /// </summary>
    private void UpdateAiJsonBufferStatus()
    {
        // _txtAiSummary.Text = BuildAiJsonSummaryText(_txtAi.Text);
    }

    /// <summary>
    /// 处理 JSON 弹窗返回的动作，统一承接保存、预览和应用。
    /// </summary>
    private void HandleAiJsonDialogAction(AiJsonDialogAction action)
    {
        switch (action)
        {
            case AiJsonDialogAction.Preview:
                if (!string.IsNullOrWhiteSpace(_txtAi.Text))
                {
                    PreviewAiJsonText(_txtAi.Text, saveToSidebar: true);
                }
                break;
            case AiJsonDialogAction.Apply:
                ApplyCurrentAiJson();
                break;
        }
    }

    /// <summary>
    /// 启动 MyChrome 浏览器；若未发现可执行文件，则先自动编译再启动。
    /// </summary>
    private async Task LaunchMyChromeAsync()
    {
        try
        {
            var projectPath = ResolveMyChromeProjectPath();
            if (projectPath == null)
            {
                Warn("未找到 MyChrome 项目文件：MyWebView2Browser.csproj");
                return;
            }

            var exePath = FindMyChromeExecutable(projectPath);
            var needsBuild = exePath == null || IsMyChromeBuildOutdated(projectPath, exePath);
            if (needsBuild)
            {
                Log(exePath == null
                    ? "· 未找到 MyChrome 可执行文件，准备自动编译 Release。"
                    : "· 检测到 MyChrome 源码已更新，准备重新编译后再启动。");
                _lblStatus.Text = "● 正在编译 MyChrome";
                _lblStatus.ForeColor = Accent;

                var buildOk = await BuildMyChromeAsync(projectPath);
                if (!buildOk) return;

                exePath = FindMyChromeExecutable(projectPath);
                if (exePath == null)
                {
                    Warn("MyChrome 编译完成，但未找到可执行文件。");
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                Warn("未找到可启动的 MyChrome 可执行文件。");
                return;
            }

            var exeDirectory = Path.GetDirectoryName(exePath) ?? Path.GetDirectoryName(projectPath) ?? "";
            var psi = new System.Diagnostics.ProcessStartInfo(exePath)
            {
                WorkingDirectory = exeDirectory,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);

            _lblStatus.Text = "● 已启动 MyChrome";
            _lblStatus.ForeColor = Success;
            Log($"✔ 已启动 MyChrome：{exePath}");
        }
        catch (Exception ex)
        {
            Warn("启动 MyChrome 失败：" + ex.Message);
        }
    }

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

        var result = await RunProcessCaptureAsync(psi, timeoutMs: 120_000);
        if (!string.IsNullOrWhiteSpace(result.StdOut)) Log("[MyChrome] " + result.StdOut.Trim());
        if (!string.IsNullOrWhiteSpace(result.StdErr)) Log("[MyChrome] " + result.StdErr.Trim());

        if (result.TimedOut)
        {
            Warn("MyChrome 编译超时，120 秒内未完成。");
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
        _tree.MouseUp += Tree_MouseUp;
        _tree.ContextMenuStrip = _treeMenu;
        
        // 文件树右键菜单
        _treeMenu.Items.Add("重命名", null, (_, _) => RenameSelectedNode());
        _treeMenu.Items.Add("删除", null, (_, _) => DeleteSelectedNode());
        _treeMenu.Items.Add(new ToolStripSeparator());
        _treeMenu.Items.Add("设置回收站目录", null, (_, _) => SetDeletedFilesBackupDir());
        _treeMenu.Opening += (_, e) => 
        {
            // 只要是在树上右击就允许打开，因为“设置回收站目录”不需要选中具体文件
        };

        leftTopPanel.Controls.Add(_lblTree, 0, 0);
        leftTopPanel.Controls.Add(_tree, 0, 1);
        leftSplit.Panel1.Controls.Add(leftTopPanel);

        // 左下：我的计划 + AI JSON 按钮
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
            Text = "📥 粘贴/编辑 JSON",
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
            Text = "🔍 预览 JSON",
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
        aiButtonPanel.Controls.Add(btnQuoteContext);

        planPanel.Controls.Add(aiButtonPanel, 0, 0);

        _txtPlan.Dock = DockStyle.Fill;
        _txtPlan.BorderStyle = BorderStyle.FixedSingle;
        planPanel.Controls.Add(_txtPlan, 0, 1);

        var btnSendPlan = new Button
        {
            Text = "✨ 生成并复制提示词",
            Dock = DockStyle.Right,
            Width = 160,
            Height = 32,
            Margin = new Padding(0, 8, 0, 0),
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
        planPanel.Controls.Add(btnSendPlan, 0, 2);
        
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

        var runHintLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "▶ 命令输出区：单独执行编译或运行，并把结果一键回传给 AI。",
            ForeColor = FgMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
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
        var btnClearOutput = createRunPanelButton("🧹 清空输出", Color.FromArgb(90, 45, 45), (_, _) => _txtRunOutput.Clear(), 110);

        runButtonPanel.Controls.Add(btnBuildOutput);
        runButtonPanel.Controls.Add(btnRunOutput);
        runButtonPanel.Controls.Add(btnCopyOutput);
        runButtonPanel.Controls.Add(btnClearOutput);
        runToolbar.Controls.Add(runHintLabel, 0, 0);
        runToolbar.Controls.Add(runButtonPanel, 1, 0);

        var runOutputPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgDark,
            Padding = new Padding(1, 8, 1, 1)
        };
        runOutputPanel.Controls.Add(_txtRunOutput);

        pnlOutputRun.Controls.Add(runToolbar, 0, 0);
        pnlOutputRun.Controls.Add(runOutputPanel, 0, 1);

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

    private enum RightPanelSection
    {
        SavedJson,
        SavedCommands,
        Log
    }

    private enum AiJsonDialogAction
    {
        None,
        Save,
        Preview,
        Apply
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

        UpdateRightPanelHeaders();
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

        PersistRightPanelCollapseState();
        ApplyRightPanelLayout();
    }

    /// <summary>
    /// 根据当前列表和日志状态刷新右侧标题中的数量提示。
    /// </summary>
    private void UpdateRightPanelHeaders()
    {
        var allJsonCount = string.IsNullOrWhiteSpace(_projectRoot) ? 0 : _settings.GetSavedJsonHistory(_projectRoot).Count;
        var allCommandCount = string.IsNullOrWhiteSpace(_projectRoot) ? 0 : _settings.GetSavedCommands(_projectRoot).Count;
        var logCount = GetLogEntryCount();
        var jsonBadge = _currentSavedJsonHistory.Count == allJsonCount
            ? $"[{allJsonCount}]"
            : $"[{_currentSavedJsonHistory.Count}/{allJsonCount}]";
        var commandBadge = _currentSavedCommands.Count == allCommandCount
            ? $"[{allCommandCount}]"
            : $"[{_currentSavedCommands.Count}/{allCommandCount}]";

        _lblSavedJsonHeader.Text = $"  {(_isSavedJsonCollapsed ? "▶" : "▼")}  AI JSON 历史 {jsonBadge}";
        _lblSavedCommandsHeader.Text = $"  {(_isSavedCommandsCollapsed ? "▶" : "▼")}  AI 命令收藏 {commandBadge}";
        _lblLogHeader.Text = $"  {(_isLogCollapsed ? "▶" : "▼")}  操作日志 [{logCount}]";
    }

    /// <summary>
    /// 统计右侧日志中的有效条目数，用于显示标题徽标。
    /// </summary>
    private int GetLogEntryCount()
    {
        return _txtLog.Lines.Count(line => !string.IsNullOrWhiteSpace(line));
    }

    /// <summary>
    /// 保存右侧面板折叠状态，确保下次启动时恢复相同布局。
    /// </summary>
    private void PersistRightPanelCollapseState()
    {
        _settings.IsSavedJsonCollapsed = _isSavedJsonCollapsed;
        _settings.IsSavedCommandsCollapsed = _isSavedCommandsCollapsed;
        _settings.IsLogCollapsed = _isLogCollapsed;
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
    /// 为 AI JSON 编辑区启动一次延迟同步，避免用户粘贴时频繁刷新右侧面板。
    /// </summary>
    private void ScheduleAiJsonSidebarSync()
    {
        _aiJsonSidebarSyncTimer.Stop();
        _aiJsonSidebarSyncTimer.Start();
    }

    /// <summary>
    /// 当用户把完整 AI JSON 粘贴到编辑区后，自动同步到右侧历史面板。
    /// </summary>
    private void TrySaveCurrentAiJsonToSidebar()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;

        var jsonText = (_txtAi.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(jsonText)) return;
        if (string.Equals(jsonText, _lastSidebarSyncedAiJson, StringComparison.Ordinal)) return;

        var looksComplete =
            (jsonText.StartsWith("{") && jsonText.EndsWith("}")) ||
            (jsonText.StartsWith("```json", StringComparison.OrdinalIgnoreCase) && jsonText.EndsWith("```", StringComparison.Ordinal));
        if (!looksComplete) return;

        try
        {
            var applier = new ChangeApplier(_projectRoot);
            var plan = applier.ParseJson(jsonText);
            var saved = _settings.SaveJsonHistory(new SavedAiJson
            {
                ProjectRoot = _projectRoot,
                Task = plan.Task,
                JsonText = jsonText,
                ChangeCount = plan.Changes.Count,
                CommandCount = plan.Commands.Count
            });

            RefreshSavedJsonPanel();
            var index = _currentSavedJsonHistory.FindIndex(c => string.Equals(c.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _lstSavedJson.SelectedIndex = index;
            _lastSidebarSyncedAiJson = jsonText;
        }
        catch
        {
            // 用户仍在编辑或粘贴不完整 JSON 时不打断输入
        }
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
        _lblStatus.Text = "● 已回填 AI JSON";
        _lblStatus.ForeColor = Accent;
        Log($"✔ 已将右侧 AI JSON 回填到编辑区：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
        UpdateAiJsonBufferStatus();
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
    /// 在 AI JSON 历史列表中按 Ctrl+C 时，直接复制当前选中 JSON。
    /// </summary>
    private void SavedJsonList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!(e.Control && e.KeyCode == Keys.C)) return;

        e.SuppressKeyPress = true;
        CopySelectedSavedJson();
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

        if (_txtPlan.ContainsFocus)
            return _txtPlan;
            
        if (_txtAi.ContainsFocus)
            return _txtAi;
            
        return _txtPlan;
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
                }
            }
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

    /// <summary>
    /// 切换到底部命令输出区，并按需写入新的阶段标题。
    /// </summary>
    private bool PrepareRunOutputPanel(string phaseTitle, bool clearOutput)
    {
        if (string.IsNullOrEmpty(_projectRoot))
        {
            MessageBox.Show("请先打开一个项目目录！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (clearOutput)
        {
            _txtRunOutput.Clear();
        }

        AppendRunOutput($"[{DateTime.Now:HH:mm:ss}] {phaseTitle}\n");
        return true;
    }

    /// <summary>
    /// 获取当前项目默认输出程序和编译参数。
    /// </summary>
    private bool TryGetCppBuildContext(out string exePath, out string filesArgs)
    {
        exePath = Path.Combine(_projectRoot ?? "", "app_run.exe");
        filesArgs = "";

        if (string.IsNullOrEmpty(_projectRoot)) return false;

        var cppFiles = Directory.GetFiles(_projectRoot, "*.cpp", SearchOption.TopDirectoryOnly);
        if (cppFiles.Length == 0)
        {
            AppendRunOutput("❌ 错误：在当前目录下未找到任何 .cpp 文件。\n");
            return false;
        }

        filesArgs = string.Join(" ", cppFiles.Select(f => $"\"{Path.GetFileName(f)}\""));
        return true;
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
            AppendRunOutput($"❌ 运行阶段发生异常：{ex.Message}\n");
            return false;
        }
    }

    /// <summary>
    /// 按经典工作流先编译再运行，适合作为一键验证入口。
    /// </summary>
    private async void RunCppCode()
    {
        if (!PrepareRunOutputPanel("准备编译并运行 C++ 代码...", clearOutput: true)) return;
        if (!TryGetCppBuildContext(out var exePath, out var filesArgs)) return;

        try
        {
            var buildOk = await BuildCppProjectCoreAsync(exePath, filesArgs);
            if (!buildOk) return;

            AppendRunOutput(new string('-', 40) + "\n");
            await RunCppProjectCoreAsync(exePath);
        }
        catch (Exception ex)
        {
            AppendRunOutput($"❌ 发生异常：{ex.Message}\n");
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

        var compiler = DetectCompiler();
        AppendRunOutput($"> {compiler} {filesArgs} -o {Path.GetFileName(exePath)}\n");

        var buildInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = compiler,
            Arguments = $"{filesArgs} -o \"{Path.GetFileName(exePath)}\"",
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
            return false;
        }

        if (buildResult.ExitCode != 0)
        {
            AppendRunOutput($"❌ 编译失败，退出码：{buildResult.ExitCode}\n");
            return false;
        }

        AppendRunOutput($"✅ 编译成功，输出：{exePath}\n");
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

        var runResult = await RunProcessCaptureAsync(runInfo, timeoutMs: 30_000);
        if (!string.IsNullOrEmpty(runResult.StdOut)) AppendRunOutput(runResult.StdOut);
        if (!string.IsNullOrEmpty(runResult.StdErr)) AppendRunOutput(runResult.StdErr);

        if (runResult.TimedOut)
        {
            AppendRunOutput("\n" + new string('-', 40) + "\n⚠ 运行超时：30 秒内未退出，已自动终止程序。\n");
            return false;
        }

        AppendRunOutput("\n" + new string('-', 40) + $"\n✅ 运行结束，退出码：{runResult.ExitCode}\n");
        return runResult.ExitCode == 0;
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
        _lastSidebarSyncedAiJson = "";
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

    private void SetDeletedFilesBackupDir()
    {
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            "请输入删除文件时移动到的回收站目录：\n留空则使用默认配置 (AppData 下的 MyIDE/DeletedFiles)。",
            "设置回收站目录",
            _settings.DeletedFilesBackupPath);

        if (input != "")
        {
            _settings.DeletedFilesBackupPath = input;
            _settings.Save();
            Log($"✔ 回收站目录已更新为: {input}");
        }
        else if (input == "" && !string.IsNullOrEmpty(_settings.DeletedFilesBackupPath))
        {
            _settings.DeletedFilesBackupPath = "";
            _settings.Save();
            Log("✔ 回收站目录已恢复为默认。");
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
                    string backupRoot = _settings.DeletedFilesBackupPath;
                    if (string.IsNullOrWhiteSpace(backupRoot))
                    {
                        backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyIDE", "DeletedFiles");
                    }
                    
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
                    
                    // 刷新文件树
                    if (_projectRoot != null) LoadDirectory(_projectRoot);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
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
                    if (p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
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
                if (p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = Path.GetRelativePath(_projectRoot, p).Replace('\\', '/');
                if (!list.Contains(rel)) list.Add(rel);
            }
        }

        // 3. 如果还是没有，获取当前在树中选中的文件
        if (list.Count == 0 && GetCurrentSelectedFile() is string sel && !list.Contains(sel))
        {
            var p = Path.Combine(_projectRoot, sel);
            if (!p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(sel);
            }
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
        var currentPrompt = prompt;
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
            Text = currentPrompt,
            ReadOnly = true,
            BackColor = BgDark,
            ForeColor = FgText,
            BorderStyle = BorderStyle.None,
        };
        var buttonPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            ColumnCount = 2,
            BackColor = BgPanel,
            Padding = new Padding(8, 6, 8, 6),
        };
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var btnAddProtocol = new Button
        {
            Text = PromptGenerator.ContainsFullJsonProtocol(currentPrompt) ? "✅ 已含 JSON 协议" : "🧩 加入 JSON 协议",
            Dock = DockStyle.Fill,
            BackColor = BgHeader,
            ForeColor = FgText,
            FlatStyle = FlatStyle.Flat,
            Enabled = !PromptGenerator.ContainsFullJsonProtocol(currentPrompt),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(0, 0, 6, 0),
        };
        var btnCopy = new Button
        {
            Text = "📋 复制到剪贴板",
            Dock = DockStyle.Fill,
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Margin = new Padding(6, 0, 0, 0),
        };
        btnAddProtocol.FlatAppearance.BorderSize = 0;
        btnCopy.FlatAppearance.BorderSize = 0;
        btnAddProtocol.Click += (_, _) =>
        {
            currentPrompt = PromptGenerator.EnsureFullJsonProtocol(tb.Text);
            tb.Text = currentPrompt;
            _lastGeneratedPrompt = currentPrompt;
            btnAddProtocol.Text = "✅ 已含 JSON 协议";
            btnAddProtocol.Enabled = false;
            Log("✔ 已将完整 JSON 协议说明加入当前提示词窗口，可直接复制给 AI。");
        };
        btnCopy.Click += (_, _) =>
        {
            try
            {
                currentPrompt = tb.Text ?? "";
                Clipboard.SetText(currentPrompt);
                _lastGeneratedPrompt = currentPrompt;
                btnCopy.Text = "✅ 已复制！";
                Log("✔ 已复制提示词，请粘贴给 AI；拿到 JSON 后粘回当前页并点“应用”");
            }
            catch (Exception ex) { MessageBox.Show("复制失败：" + ex.Message); }
        };
        buttonPanel.Controls.Add(btnAddProtocol, 0, 0);
        buttonPanel.Controls.Add(btnCopy, 1, 0);
        dlg.Controls.Add(tb);
        dlg.Controls.Add(buttonPanel);
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
        var includeJsonProtocol = _settings.IncludePromptProtocolOnNextPrompt;
        var prompt = gen.Generate(_projectRoot, files, _txtPlan.Text, _chkIncludeAll.Checked, includeJsonProtocol);
        if (includeJsonProtocol)
        {
            _settings.IncludePromptProtocolOnNextPrompt = false;
            _settings.Save();
        }
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
            Log("下一步：把提示词发给 AI，然后将返回 JSON 粘贴");
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
    /// 从 MyChrome 当前 AI 页面读取最新一条回复，优先提取页面原生复制结果供 IDE 直接回填。
    /// </summary>
    private async Task<AiBrowserReplyResult> TryReadLatestReplyFromAiBrowserAsync()
    {
        try
        {
            using var response = await AiBrowserLatestReplyHttpClient.GetAsync("http://127.0.0.1:18888/api/ai/latest-reply");
            var responseText = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AiBrowserReplyResult>(responseText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result != null)
            {
                return result;
            }

            return new AiBrowserReplyResult
            {
                Ok = false,
                Reason = "script_result_empty",
                Message = "MyChrome 返回了空结果"
            };
        }
        catch (Exception ex)
        {
            return new AiBrowserReplyResult
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
        if (string.IsNullOrWhiteSpace(_txtAi.Text))
        {
            var action = ShowAiJsonInputDialog();
            if (action == AiJsonDialogAction.None) return;
            if (action == AiJsonDialogAction.Apply)
            {
                ApplyCurrentAiJson();
                return;
            }
            if (action == AiJsonDialogAction.Preview)
            {
                PreviewAiJsonText(_txtAi.Text, saveToSidebar: true);
                return;
            }
            if (string.IsNullOrWhiteSpace(_txtAi.Text)) return;
        }
        PreviewAiJsonText(_txtAi.Text, saveToSidebar: true);
    }

    /// <summary>解析 → 模拟 → 弹出 diff → 用户确认 → 应用</summary>
    private void BtnApply_Click(object? sender, EventArgs e)
    {
        ApplyCurrentAiJson();
    }

    /// <summary>
    /// 解析并应用当前暂存的 AI JSON，供主界面和弹窗按钮复用。
    /// </summary>
    private void ApplyCurrentAiJson()
    {
        if (_projectRoot == null) { Warn("请先打开目录"); return; }
        if (string.IsNullOrWhiteSpace(_txtAi.Text))
        {
            var action = ShowAiJsonInputDialog();
            if (action == AiJsonDialogAction.None)
            {
                Warn("AI 返回内容为空");
                return;
            }
            if (action == AiJsonDialogAction.Preview)
            {
                PreviewAiJsonText(_txtAi.Text, saveToSidebar: true);
                return;
            }
            if (string.IsNullOrWhiteSpace(_txtAi.Text))
            {
                Warn("AI 返回内容为空");
                return;
            }
        }

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
        UpdateRightPanelHeaders();
    }

    /// <summary>
    /// 清空右侧日志内容，并同步刷新标题数量提示。
    /// </summary>
    private void ClearLogPanel()
    {
        _txtLog.Clear();
        UpdateRightPanelHeaders();
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
