using System;
using System.Collections.Generic;
using System.IO;

namespace MyIDE.Services;

/// <summary>
/// 撤销栈：每次应用修改前，把所有受影响文件的原内容压栈；撤销时还原
/// </summary>
public class UndoStack
{
    /// <summary>单条撤销记录</summary>
    public class Snapshot
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Description { get; set; } = "";
        public List<FileBackup> Files { get; set; } = new();
    }

    /// <summary>单文件的备份</summary>
    public class FileBackup
    {
        public string Path { get; set; } = "";
        public string? Content { get; set; }  // null 表示文件原本不存在
        public bool Existed { get; set; }
    }

    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private const int MaxDepth = 50;

    /// <summary>当前可撤销的步数</summary>
    public int UndoCount => _undo.Count;
    /// <summary>当前可重做的步数</summary>
    public int RedoCount => _redo.Count;

    /// <summary>
    /// 在应用一组修改前调用，预先记录所有受影响文件的当前内容
    /// </summary>
    public Snapshot BeginSnapshot(string description)
    {
        return new Snapshot { Description = description };
    }

    /// <summary>把一个文件加入当前快照</summary>
    public void RecordFile(Snapshot snap, string path)
    {
        // 同一文件在同一快照内只记录一次（取最早的内容）
        foreach (var fb in snap.Files)
            if (string.Equals(fb.Path, path, StringComparison.OrdinalIgnoreCase)) return;

        var backup = new FileBackup { Path = path };
        if (File.Exists(path))
        {
            backup.Existed = true;
            backup.Content = File.ReadAllText(path);
        }
        snap.Files.Add(backup);
    }

    /// <summary>把快照提交进栈</summary>
    public void Commit(Snapshot snap)
    {
        _undo.Push(snap);
        _redo.Clear();
        // 限制最大深度
        while (_undo.Count > MaxDepth)
        {
            var tmp = new Stack<Snapshot>(_undo);
            tmp.Pop(); // 弹出最老的
            _undo.Clear();
            foreach (var s in tmp) _undo.Push(s);
            break;
        }
    }

    /// <summary>撤销一步，返回被撤销的快照（如果没东西可撤销则返回 null）</summary>
    public Snapshot? Undo()
    {
        if (_undo.Count == 0) return null;
        var snap = _undo.Pop();
        foreach (var fb in snap.Files)
        {
            try
            {
                if (fb.Existed && fb.Content != null)
                {
                    File.WriteAllText(fb.Path, fb.Content);
                }
                else if (!fb.Existed && File.Exists(fb.Path))
                {
                    File.Delete(fb.Path);
                }
            }
            catch
            {
                // 某个文件还原失败，继续处理其它文件
            }
        }
        _redo.Push(snap);
        return snap;
    }

    /// <summary>重做一步</summary>
    public Snapshot? Redo()
    {
        if (_redo.Count == 0) return null;
        var snap = _redo.Pop();
        // 重做需要再次应用——但我们没存"修改后的内容"，简化处理：重做只记录，不真做
        // 因为我们存的是原内容，重做需要 ChangePlan。v1 暂不实现真重做
        return snap;
    }

    /// <summary>清空所有栈</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
