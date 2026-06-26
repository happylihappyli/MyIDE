using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MyIDE.Services;

namespace MyIDE.Forms;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _txtBackupPath;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "系统设置";
        Size = new Size(500, 200);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;

        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyIDE", "DeletedFiles");
        
        var lblBackup = new Label
        {
            Text = $"删除文件回收站目录 (留空使用默认):\n{defaultPath}",
            Location = new Point(20, 15),
            AutoSize = true,
            ForeColor = Color.White
        };

        _txtBackupPath = new TextBox
        {
            Location = new Point(20, 55),
            Width = 360,
            Text = _settings.DeletedFilesBackupPath,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var btnBrowse = new Button
        {
            Text = "浏览...",
            Location = new Point(390, 54),
            Width = 70,
            Height = 25,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        btnBrowse.FlatAppearance.BorderSize = 0;
        btnBrowse.Click += BtnBrowse_Click;

        var btnSave = new Button
        {
            Text = "保存",
            Location = new Point(300, 110),
            Width = 80,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(390, 110),
            Width = 80,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        btnCancel.FlatAppearance.BorderSize = 0;
        btnCancel.Click += (_, _) => Close();

        Controls.Add(lblBackup);
        Controls.Add(_txtBackupPath);
        Controls.Add(btnBrowse);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "请选择回收站备份目录"
        };
        
        if (!string.IsNullOrWhiteSpace(_txtBackupPath.Text) && Directory.Exists(_txtBackupPath.Text))
        {
            fbd.SelectedPath = _txtBackupPath.Text;
        }

        if (fbd.ShowDialog() == DialogResult.OK)
        {
            _txtBackupPath.Text = fbd.SelectedPath;
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        _settings.DeletedFilesBackupPath = _txtBackupPath.Text.Trim();
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}