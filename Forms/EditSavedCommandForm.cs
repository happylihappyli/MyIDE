using System;
using System.Drawing;
using System.Windows.Forms;
using MyIDE.Models;

namespace MyIDE.Forms;

public class EditSavedCommandForm : Form
{
    private readonly SavedAiCommand _command;
    
    private readonly TextBox _txtName = new TextBox();
    private readonly TextBox _txtReason = new TextBox();
    private readonly TextBox _txtShell = new TextBox();
    private readonly TextBox _txtWorkingDir = new TextBox();
    private readonly TextBox _txtCommand = new TextBox();
    
    public EditSavedCommandForm(SavedAiCommand command)
    {
        _command = command;
        
        Text = "编辑收藏命令";
        Width = 600;
        Height = 450;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(15)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        AddRow(panel, 0, "名称:", _txtName, command.Name);
        AddRow(panel, 1, "用途:", _txtReason, command.Reason);
        AddRow(panel, 2, "Shell:", _txtShell, command.Shell);
        AddRow(panel, 3, "目录:", _txtWorkingDir, command.WorkingDirectory);
        
        _txtCommand.Multiline = true;
        _txtCommand.ScrollBars = ScrollBars.Vertical;
        _txtCommand.Font = new Font("Cascadia Mono", 10);
        AddRow(panel, 4, "命令内容:", _txtCommand, command.Command);
        _txtCommand.Dock = DockStyle.Fill;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 5, 0, 0)
        };
        
        var btnCancel = new Button { Text = "取消", Width = 80, Height = 30 };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        
        var btnSave = new Button { Text = "保存", Width = 80, Height = 30, BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += BtnSave_Click;

        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnSave);
        
        panel.Controls.Add(btnPanel, 1, 5);
        
        Controls.Add(panel);
    }

    private void AddRow(TableLayoutPanel panel, int row, string labelText, TextBox textBox, string initialValue)
    {
        var lbl = new Label { Text = labelText, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
        textBox.Text = initialValue;
        textBox.Dock = DockStyle.Fill;
        
        panel.Controls.Add(lbl, 0, row);
        panel.Controls.Add(textBox, 1, row);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        _command.Name = _txtName.Text;
        _command.Reason = _txtReason.Text;
        _command.Shell = _txtShell.Text;
        _command.WorkingDirectory = _txtWorkingDir.Text;
        _command.Command = _txtCommand.Text;
        
        DialogResult = DialogResult.OK;
        Close();
    }
}