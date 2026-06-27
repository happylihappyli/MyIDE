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

    /// <summary>
    /// 新开一个 AI Session，让下一次生成提示词时重新包含完整 Patch 协议说明。
    /// </summary>
    private void StartNewPromptSession()
    {
        _settings.IncludePromptProtocolOnNextPrompt = true;
        _settings.Save();
        _lastGeneratedPrompt = "";
        _lblStatus.Text = "● 已新建 Session";
        _lblStatus.ForeColor = Accent;
        Log("✔ 已标记为新 Session；下一次生成提示词将重新附带完整 Patch 协议说明。");
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
    /// 把当前计划内容写入设置，保证下次打开时自动恢复；仅在显式要求时才加入计划历史。
    /// </summary>
    private void SaveCurrentPlanText(bool forceHistory = false)
    {
        var planText = _txtPlan.Text ?? "";
        var changed = !string.Equals(_settings.LastPlanText, planText, StringComparison.Ordinal);
        if (changed)
        {
            _settings.LastPlanText = planText;
            _settings.Save();
        }

        if (forceHistory)
        {
            SaveCurrentPlanToHistory(force: true);
        }
    }

    /// <summary>
    /// 打开 AI 返回内容粘贴对话框，支持直接保存、预览或应用。
    /// </summary>
    private AiJsonDialogAction ShowAiJsonInputDialog()
    {
        AiJsonDialogAction action = AiJsonDialogAction.None;
        var recentHistory = GetRecentSavedJsonHistory(3);
        using var dlg = new Form
        {
            Text = "粘贴 AI 返回内容",
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
            Text = "左侧默认保持空白，可直接粘贴或从 MyChrome 读取最新回复；如需恢复上次暂存内容，可点“读取上次内容”。右侧可查看解析摘要、命令区快捷提示，并从最近 3 条历史一键带入。快捷键：Ctrl+Enter 应用，Ctrl+Shift+Enter 预览，Ctrl+M 读取，Ctrl+Delete 清空。"
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
            Text = ""
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
                "1. 只想执行命令也可以，推荐直接使用方案 A 的【任务】+【命令】模板。\r\n" +
                "2. 点击“保存并应用”后，会直接进入命令执行窗口，不再先弹 Diff。\r\n" +
                "3. 这样更适合把编译、运行、测试命令直接交给 MyIDE 执行。\r\n" +
                "4. Ctrl+L 可一键插入命令模板；返回内容需要原样保留时，不再提供格式化按钮。"
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
            Text = "最近 3 条 AI 返回历史"
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
        var btnLoadLastBuffer = makeButton("读取上次内容", Color.FromArgb(96, 72, 138));
        btnLoadLastBuffer.Width = 128;
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
                    ? "当前还没有打开项目，无法读取 AI 返回历史。"
                    : "当前项目还没有可复用的 AI 返回历史。";
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
                Warn("当前没有可带入的 AI 返回历史");
                return;
            }

            tb.Text = item.JsonText;
            tb.SelectionStart = tb.TextLength;
            tb.SelectionLength = 0;
            tb.Focus();
            Log($"✔ 已从最近历史带入 AI 返回内容：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
        }

        void insertCommandsTemplate()
        {
            tb.Text = BuildCommandsOnlyPatchTemplate();
            tb.SelectionStart = tb.TextLength;
            tb.SelectionLength = 0;
            tb.Focus();
            Log("✔ 已插入仅命令的 Patch 协议模板。");
        }

        void loadLastBuffer()
        {
            if (string.IsNullOrWhiteSpace(_txtAi.Text))
            {
                Warn("当前没有上次暂存的 AI 返回内容");
                return;
            }

            tb.Text = _txtAi.Text;
            tb.SelectionStart = tb.TextLength;
            tb.SelectionLength = 0;
            tb.Focus();
            Log("✔ 已读取上次暂存的 AI 返回内容。");
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
                    var extracted = ExtractAiResponseBody(replyText);
                    importedText = NormalizeAiResponseBody(replyText);
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
            Log("✔ 已清空左侧返回内容编辑区。");
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
        btnLoadLastBuffer.Click += (_, _) => loadLastBuffer();
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
        quickActionPanel.Controls.Add(btnLoadLastBuffer);
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
    /// 清空当前暂存的 AI 返回内容，并刷新下方摘要状态。
    /// </summary>
    private void ClearCurrentAiJson()
    {
        _txtAi.Clear();
        UpdateAiJsonBufferStatus();
        Log("✔ 已清空当前暂存的 AI 返回内容。");
    }

    /// <summary>
    /// 构建 AI 返回内容的摘要文本，供主页和弹窗右侧统一复用。
    /// </summary>
    private string BuildAiJsonSummaryText(string jsonText, bool showRawHiddenHint = true)
    {
        var normalized = (jsonText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "当前没有暂存的 AI 返回内容。\r\n\r\n点击“粘贴 / 编辑返回内容”按钮后，会弹出对话框供你粘贴内容。";
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
                    summary += "\r\n\r\n原始返回内容已隐藏，不再直接显示在下方页签区域。";
                }
                if (plan.Changes.Count == 0 && plan.Commands.Count > 0)
                {
                    summary += "\r\n\r\n快捷提示：当前只有 commands 区段，点击“保存并应用”会直接进入命令执行窗口。";
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
                "可以继续编辑返回内容，修正后这里会自动刷新摘要。";
        }

        return
            $"当前已暂存 AI 返回内容\r\n长度：{normalized.Length} 字符" +
            (showRawHiddenHint ? "\r\n\r\n原始返回内容已隐藏，不再直接显示在下方页签区域。" : "");
    }

    /// <summary>
    /// 用摘要而不是原始全文显示当前暂存的 AI 返回内容状态。
    /// </summary>
    private void UpdateAiJsonBufferStatus()
    {
        // _txtAiSummary.Text = BuildAiJsonSummaryText(_txtAi.Text);
    }

    /// <summary>
    /// 处理返回内容弹窗返回的动作，统一承接保存、预览和应用。
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
    /// 将当前 AI 返回内容保存到右侧历史区。
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
        Log($"✔ 已保存 AI 返回内容到右侧历史区：任务={saved.Task}，修改={saved.ChangeCount}，命令={saved.CommandCount}");
    }

    /// <summary>
    /// 为 AI 返回内容编辑区启动一次延迟同步，避免用户粘贴时频繁刷新右侧面板。
    /// </summary>
    private void ScheduleAiJsonSidebarSync()
    {
        _aiJsonSidebarSyncTimer.Stop();
        _aiJsonSidebarSyncTimer.Start();
    }

    /// <summary>
    /// 当用户把完整 AI 返回内容粘贴到编辑区后，自动同步到右侧历史面板。
    /// </summary>
    private void TrySaveCurrentAiJsonToSidebar()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;

        var jsonText = (_txtAi.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(jsonText)) return;
        if (string.Equals(jsonText, _lastSidebarSyncedAiJson, StringComparison.Ordinal)) return;

        var looksComplete =
            LooksLikePatchResponse(jsonText) ||
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
            // 用户仍在编辑或粘贴不完整返回内容时不打断输入
        }
    }

    /// <summary>
    /// 把右侧选中的 AI 返回内容回填到当前编辑框。
    /// </summary>
    private void UseSelectedSavedJson()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI 返回内容");
            return;
        }

        _txtAi.Text = item.JsonText;
        _lblStatus.Text = "● 已回填 AI 返回内容";
        _lblStatus.ForeColor = Accent;
        Log($"✔ 已将右侧 AI 返回内容回填到编辑区：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
        UpdateAiJsonBufferStatus();
    }

    /// <summary>
    /// 双击右侧 AI 返回内容时，直接回填并打开预览。
    /// </summary>
    private void PreviewSelectedSavedJson()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI 返回内容");
            return;
        }

        UseSelectedSavedJson();
        PreviewAiJsonText(item.JsonText, saveToSidebar: false);
    }

    /// <summary>
    /// 复制右侧选中的 AI 返回内容文本。
    /// </summary>
    private void CopySelectedSavedJson()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI 返回内容");
            return;
        }

        try
        {
            Clipboard.SetText(item.JsonText);
            Log($"✔ 已复制右侧 AI 返回内容：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
        }
        catch (Exception ex)
        {
            Warn("复制 AI 返回内容失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 删除右侧选中的 AI 返回内容记录。
    /// </summary>
    private void DeleteSelectedSavedJson()
    {
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI 返回内容");
            return;
        }

        _settings.RemoveSavedJsonHistory(item.Id);
        RefreshSavedJsonPanel();
        Log($"✔ 已删除右侧 AI 返回内容：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
    }

    /// <summary>
    /// 切换右侧选中 AI 返回内容的置顶状态。
    /// </summary>
    private void ToggleSelectedSavedJsonPinned()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot)) return;
        var item = GetSelectedSavedJson();
        if (item == null)
        {
            Warn("请先在右侧选择一条 AI 返回内容");
            return;
        }

        var nextPinned = !item.IsPinned;
        _settings.SetSavedJsonPinned(_projectRoot, item.Id, nextPinned);
        RefreshSavedJsonPanel();
        var index = _currentSavedJsonHistory.FindIndex(c => string.Equals(c.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _lstSavedJson.SelectedIndex = index;
        Log(nextPinned
            ? $"✔ 已置顶右侧 AI 返回内容：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}"
            : $"✔ 已取消置顶右侧 AI 返回内容：{(string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task)}");
    }

    /// <summary>
    /// 解析并预览指定的 AI 返回内容，供按钮和侧栏双击复用。
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
    /// 解析并应用当前暂存的 AI 返回内容，供主界面和弹窗按钮复用。
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
            Log($"· 本次返回内容没有文件修改，直接进入命令执行流程：命令数={plan.Commands.Count}");
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

        if (summary.SuccessOps < summary.TotalOps)
        {
            MessageBox.Show(
                this,
                BuildApplyFailureMessage(summary),
                "应用修改遇到问题",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (plan.Commands.Count > 0)
        {
            Log($"· AI 还返回了 {plan.Commands.Count} 条命令，准备打开命令执行窗口。");
            using var commandForm = new AiCommandBatchForm(_projectRoot, plan.Commands, SaveAiCommandsToSidebar) { Owner = this };
            commandForm.ShowDialog(this);
        }
    }

    /// <summary>
    /// 根据应用日志生成更准确的失败提示，区分补丁失配与真实写盘异常。
    /// </summary>
    private static string BuildApplyFailureMessage(ChangeApplier.ApplySummary summary)
    {
        var logs = summary.Log ?? new List<string>();

        var patchMismatch = logs.FirstOrDefault(line =>
            line.Contains("[中止写盘]", StringComparison.Ordinal) ||
            line.Contains("未找到与补丁上下文匹配的原文块", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(patchMismatch))
        {
            return
                "部分或全部修改未应用。\n\n" +
                "更可能的原因不是文件占用，而是 AI 返回的补丁和当前源码对不上，系统已为安全起见中止写盘，避免把文件改坏。\n\n" +
                $"日志摘要：{patchMismatch}\n\n" +
                "请查看日志面板定位失配的文件和片段。";
        }

        var writeFailure = logs.FirstOrDefault(line => line.Contains("[写盘失败]", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(writeFailure))
        {
            return
                "部分或全部修改应用失败。\n\n" +
                "这次更像是真实写盘失败，可能是文件被占用、没有写入权限，或目标文件正被其他程序锁定。\n\n" +
                $"日志摘要：{writeFailure}\n\n" +
                "请查看日志面板了解详细信息。";
        }

        return
            "部分或全部修改应用失败，请查看日志面板了解详细信息。\n\n" +
            "可能原因包括：补丁上下文失配、文件被占用，或没有写入权限。";
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
    /// 判断文本是否更像新的 Patch 协议响应。
    /// </summary>
    private static bool LooksLikePatchResponse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return false;

        var text = rawText.Trim();
        return text.Contains("*** Begin Patch", StringComparison.Ordinal) ||
               text.StartsWith("```patch", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("【补丁】", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从 AI 返回文本中提取最适合进入编辑框的正文；Patch 直接保留，旧 JSON 继续走提取逻辑。
    /// </summary>
    private static string ExtractAiResponseBody(string rawText)
    {
        if (LooksLikePatchResponse(rawText))
        {
            return (rawText ?? "").Trim();
        }

        return ExtractJsonBody(rawText);
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
    /// 统一规范化 AI 返回正文；Patch 直接保留，旧 JSON 在必要时自动修复字符串转义损坏。
    /// </summary>
    private static string NormalizeAiResponseBody(string rawText)
    {
        if (LooksLikePatchResponse(rawText))
        {
            return ExtractAiResponseBody(rawText);
        }

        return NormalizeAiJsonBody(rawText);
    }

    /// <summary>
    /// 统一规范化旧协议 JSON 正文，优先保留原始文本，必要时自动修复 DOM 提取造成的字符串转义损坏。
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
    /// 生成一个仅执行命令的 Patch 协议模板，便于快速走命令流。
    /// </summary>
    private static string BuildCommandsOnlyPatchTemplate()
    {
        var commands = new List<AiCommand>
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
        };
        var commandsJson = JsonSerializer.Serialize(commands, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return
            "【任务】\r\n" +
            "执行命令\r\n\r\n" +
            "【补丁】\r\n\r\n" +
            "【命令】\r\n" +
            "```json\r\n" +
            commandsJson +
            "\r\n```";
    }

    /// <summary>
    /// 生成弹窗内“最近返回历史”列表项文本，让时间和命令数量更直观。
    /// </summary>
    private static string BuildRecentSavedJsonDialogListText(SavedAiJson item)
    {
        var title = string.IsNullOrWhiteSpace(item.Task) ? "未命名任务" : item.Task;
        if (title.Length > 18) title = title[..18] + "...";
        return $"{item.UpdatedAt:MM-dd HH:mm} [C{item.ChangeCount}/M{item.CommandCount}] {title}";
    }

    /// <summary>
    /// 获取当前项目最近更新的几条 AI 返回历史，供弹窗右侧快速带入。
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
}
