using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyIDE.Models;

namespace MyIDE.Services;

/// <summary>
/// 应用设置：保存最近打开的目录等用户偏好
/// </summary>
public class AppSettings
{
    public List<string> RecentDirs { get; set; } = new();
    public List<SavedAiCommand> SavedCommands { get; set; } = new();
    public List<SavedAiJson> SavedJsonHistory { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            var path = GetPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = GetPath();
            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch { }
    }

    public void AddRecentDir(string dir)
    {
        RecentDirs.RemoveAll(d => string.Equals(d, dir, StringComparison.OrdinalIgnoreCase));
        RecentDirs.Insert(0, dir);
        if (RecentDirs.Count > 10)
        {
            RecentDirs.RemoveRange(10, RecentDirs.Count - 10);
        }
        Save();
    }

    /// <summary>
    /// 获取某个项目下保存的 AI 命令。
    /// </summary>
    public List<SavedAiCommand> GetSavedCommands(string projectRoot)
    {
        return SavedCommands
            .Where(c => string.Equals(c.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.SortOrder)
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();
    }

    /// <summary>
    /// 保存或更新一条 AI 命令。
    /// </summary>
    public SavedAiCommand SaveCommand(SavedAiCommand command)
    {
        var existing = SavedCommands.FirstOrDefault(c =>
            string.Equals(c.ProjectRoot, command.ProjectRoot, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Command, command.Command, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.WorkingDirectory ?? "", command.WorkingDirectory ?? "", StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Name = command.Name;
            existing.Reason = command.Reason;
            existing.Shell = command.Shell;
            existing.Optional = command.Optional;
            existing.IsDefaultBuild = command.IsDefaultBuild || existing.IsDefaultBuild;
            existing.IsDefaultRun = command.IsDefaultRun || existing.IsDefaultRun;
            existing.UpdatedAt = DateTime.Now;
            Save();
            return existing;
        }

        command.UpdatedAt = DateTime.Now;
        command.SortOrder = SavedCommands
            .Where(c => string.Equals(c.ProjectRoot, command.ProjectRoot, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.SortOrder)
            .DefaultIfEmpty(0)
            .Max() + 1;
        SavedCommands.Insert(0, command);
        Save();
        return command;
    }

    /// <summary>
    /// 删除一条已保存的 AI 命令。
    /// </summary>
    public void RemoveSavedCommand(string id)
    {
        SavedCommands.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>
    /// 设置项目默认编译命令。
    /// </summary>
    public void SetDefaultBuildCommand(string projectRoot, string id)
    {
        foreach (var command in SavedCommands.Where(c => string.Equals(c.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase)))
        {
            command.IsDefaultBuild = string.Equals(command.Id, id, StringComparison.OrdinalIgnoreCase);
        }
        Save();
    }

    /// <summary>
    /// 设置项目默认运行命令。
    /// </summary>
    public void SetDefaultRunCommand(string projectRoot, string id)
    {
        foreach (var command in SavedCommands.Where(c => string.Equals(c.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase)))
        {
            command.IsDefaultRun = string.Equals(command.Id, id, StringComparison.OrdinalIgnoreCase);
        }
        Save();
    }

    /// <summary>
    /// 获取项目默认编译命令。
    /// </summary>
    public SavedAiCommand? GetDefaultBuildCommand(string projectRoot)
    {
        return SavedCommands.FirstOrDefault(c =>
            string.Equals(c.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase) && c.IsDefaultBuild);
    }

    /// <summary>
    /// 获取项目默认运行命令。
    /// </summary>
    public SavedAiCommand? GetDefaultRunCommand(string projectRoot)
    {
        return SavedCommands.FirstOrDefault(c =>
            string.Equals(c.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase) && c.IsDefaultRun);
    }

    /// <summary>
    /// 获取某个项目下保存的 AI JSON 记录。
    /// </summary>
    public List<SavedAiJson> GetSavedJsonHistory(string projectRoot)
    {
        return SavedJsonHistory
            .Where(c => string.Equals(c.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.IsPinned)
            .ThenBy(c => c.SortOrder)
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();
    }

    /// <summary>
    /// 保存或更新一条 AI JSON 记录。
    /// </summary>
    public SavedAiJson SaveJsonHistory(SavedAiJson item)
    {
        var existing = SavedJsonHistory.FirstOrDefault(c =>
            string.Equals(c.ProjectRoot, item.ProjectRoot, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.JsonText, item.JsonText, StringComparison.Ordinal));

        if (existing != null)
        {
            existing.Task = item.Task;
            existing.ChangeCount = item.ChangeCount;
            existing.CommandCount = item.CommandCount;
            existing.IsPinned = item.IsPinned || existing.IsPinned;
            existing.UpdatedAt = DateTime.Now;
            Save();
            return existing;
        }

        item.UpdatedAt = DateTime.Now;
        item.SortOrder = SavedJsonHistory
            .Where(c => string.Equals(c.ProjectRoot, item.ProjectRoot, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.SortOrder)
            .DefaultIfEmpty(0)
            .Max() + 1;
        SavedJsonHistory.Insert(0, item);

        var projectItems = SavedJsonHistory
            .Where(c => string.Equals(c.ProjectRoot, item.ProjectRoot, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.SortOrder)
            .ToList();
        if (projectItems.Count > 20)
        {
            var removeIds = projectItems.Skip(20).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SavedJsonHistory.RemoveAll(c => removeIds.Contains(c.Id));
        }

        Save();
        return item;
    }

    /// <summary>
    /// 删除一条已保存的 AI JSON 记录。
    /// </summary>
    public void RemoveSavedJsonHistory(string id)
    {
        SavedJsonHistory.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>
    /// 设置或取消置顶某条 AI JSON 记录。
    /// </summary>
    public void SetSavedJsonPinned(string projectRoot, string id, bool isPinned)
    {
        foreach (var item in SavedJsonHistory.Where(c => string.Equals(c.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                item.IsPinned = isPinned;
                item.UpdatedAt = DateTime.Now;
                break;
            }
        }
        Save();
    }

    /// <summary>
    /// 调整项目内命令顺序。
    /// </summary>
    public bool MoveSavedCommand(string projectRoot, string id, int direction)
    {
        var projectCommands = SavedCommands
            .Where(c => string.Equals(c.ProjectRoot, projectRoot, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.SortOrder)
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();

        var index = projectCommands.FindIndex(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= projectCommands.Count) return false;

        (projectCommands[index], projectCommands[targetIndex]) = (projectCommands[targetIndex], projectCommands[index]);

        for (int i = 0; i < projectCommands.Count; i++)
        {
            projectCommands[i].SortOrder = i + 1;
        }

        Save();
        return true;
    }

    private static string GetPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "MyIDE", "settings.json");
    }
}
