using System;
using System.IO;
using System.Text;
using MyIDE.Models;
using MyIDE.Services;

namespace MyIDE.SmokeTest;

/// <summary>
/// 端到端冒烟测试：JSON 解析 + 修改应用 + Diff 引擎 + 撤销栈
/// </summary>
internal static class Program
{
    [STAThread]
    static int Main()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "MyIDE_SmokeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        Console.WriteLine($"测试目录：{tmp}");
        var passed = 0;
        var failed = 0;

        try
        {
            RunTest("替换+插入", () => TestReplaceAndInsert(tmp), ref passed, ref failed);
            RunTest("删除", () => TestDelete(tmp), ref passed, ref failed);
            RunTest("提示词生成", () => TestPromptGen(tmp), ref passed, ref failed);
            RunTest("Diff 引擎", TestDiffEngine, ref passed, ref failed);
            RunTest("Simulate 不落盘", () => TestSimulate(tmp), ref passed, ref failed);
            RunTest("撤销栈", () => TestUndoStack(tmp), ref passed, ref failed);
            RunTest("边界：空 ops", () => TestEmptyOps(tmp), ref passed, ref failed);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[未捕获异常] {ex}");
            failed++;
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }

        Console.WriteLine($"\n=== 通过 {passed} / 失败 {failed} ===");
        return failed == 0 ? 0 : 1;
    }

    static void RunTest(string name, Action body, ref int passed, ref int failed)
    {
        try { body(); Console.WriteLine($"  ✔ {name}"); passed++; }
        catch (Exception ex) { Console.WriteLine($"  ✖ {name}：{ex.Message}"); failed++; }
    }

    // ===== 测试用例 =====

    static void TestReplaceAndInsert(string tmp)
    {
        var target = Path.Combine(tmp, "Hello.cs");
        File.WriteAllText(target,
            "using System;\n" +
            "class Hello\n" +
            "{\n" +
            "    static void Main()\n" +
            "    {\n" +
            "        Console.WriteLine(\"old\");\n" +
            "    }\n" +
            "}\n", Encoding.UTF8);

        var fakeAi = "```json\n" +
            "{ \"task\":\"t\", \"changes\":[{" +
            "\"file\":\"Hello.cs\"," +
            "\"ops\":[" +
            "{ \"type\":\"replace\", \"start\":6, \"end\":6, \"content\":\"        Console.WriteLine(\\\"new\\\");\" }," +
            "{ \"type\":\"insert\", \"after\":8, \"content\":\"// end of file\" }" +
            "]}]}\n```";
        var applier = new ChangeApplier(tmp);
        var plan = applier.ParseJson(fakeAi);
        applier.Apply(plan, createBackup: false);
        var c = File.ReadAllText(target);
        if (!c.Contains("Console.WriteLine(\"new\")")) throw new Exception("未替换成功");
        if (c.Contains("Console.WriteLine(\"old\")")) throw new Exception("旧字符串残留");
        if (!c.Contains("// end of file")) throw new Exception("插入失败");
    }

    static void TestDelete(string tmp)
    {
        var f = Path.Combine(tmp, "Del.cs");
        File.WriteAllText(f, "a\nb\nc\nd\ne\n");
        var applier = new ChangeApplier(tmp);
        var plan = applier.ParseJson("{ \"task\":\"t\", \"changes\":[{\"file\":\"Del.cs\",\"ops\":[{\"type\":\"delete\",\"start\":2,\"end\":3}]}] }");
        applier.Apply(plan, createBackup: false);
        var c = File.ReadAllText(f);
        var expected = "a" + Environment.NewLine + "d" + Environment.NewLine + "e" + Environment.NewLine;
        if (c != expected) throw new Exception($"删除结果不符：{c.Replace("\n", "\\n")}");
    }

    static void TestPromptGen(string tmp)
    {
        var gen = new PromptGenerator();
        var p = gen.Generate(tmp, new System.Collections.Generic.List<string> { "Hello.cs" }, "做点啥", includeAllFiles: false);
        if (!p.Contains("【任务说明】") || !p.Contains("Hello.cs")) throw new Exception("提示词缺字段");
        if (p.Length < 100) throw new Exception("提示词过短");
    }

    static void TestDiffEngine()
    {
        var before = new[] { "a", "b", "c", "d" };
        var after  = new[] { "a", "B", "c", "d", "e" };
        var diff = DiffEngine.Diff(before, after);
        var (added, removed, same) = DiffEngine.Summarize(diff);
        // b -> B 是 1 删除 + 1 新增；末尾 +e 是 1 新增；LCS = {a,c,d}，所以 same=3
        if (added != 2) throw new Exception($"added 应为 2，实际 {added}");
        if (removed != 1) throw new Exception($"removed 应为 1，实际 {removed}");
        if (same != 3) throw new Exception($"same 应为 3（LCS=a,c,d），实际 {same}");
    }

    static void TestSimulate(string tmp)
    {
        var f = Path.Combine(tmp, "Sim.cs");
        File.WriteAllText(f, "line1\nline2\nline3\n");
        var applier = new ChangeApplier(tmp);
        var plan = applier.ParseJson("{ \"task\":\"t\", \"changes\":[{\"file\":\"Sim.cs\",\"ops\":[{\"type\":\"replace\",\"start\":2,\"end\":2,\"content\":\"REPLACED\"}]}] }");
        var sims = applier.Simulate(plan);
        var after = File.ReadAllText(f);
        if (after.Contains("REPLACED")) throw new Exception("Simulate 不应落盘，但文件被改了");
        if (!sims[0].NewContent.Contains("REPLACED")) throw new Exception("Simulate 未返回新内容");
    }

    static void TestUndoStack(string tmp)
    {
        var f = Path.Combine(tmp, "Undo.cs");
        File.WriteAllText(f, "original\n");
        var undo = new UndoStack();

        // 模拟一次修改
        var snap = undo.BeginSnapshot("test");
        undo.RecordFile(snap, f);
        undo.Commit(snap);
        File.WriteAllText(f, "modified\n");
        if (undo.UndoCount != 1) throw new Exception("撤销栈应为 1");

        // 撤销
        var popped = undo.Undo();
        if (popped == null || popped.Files.Count != 1) throw new Exception("撤销失败");
        var restored = File.ReadAllText(f);
        if (!restored.StartsWith("original")) throw new Exception("撤销未恢复原内容");
    }

    static void TestEmptyOps(string tmp)
    {
        var f = Path.Combine(tmp, "Empty.cs");
        File.WriteAllText(f, "x\n");
        var applier = new ChangeApplier(tmp);
        var plan = applier.ParseJson("{ \"task\":\"无需修改\", \"changes\":[] }");
        var s = applier.Apply(plan, false);
        if (s.TotalOps != 0 || s.SuccessOps != 0) throw new Exception("空 ops 期望 0/0");
        var sims = applier.Simulate(plan);
        if (sims.Count != 0) throw new Exception("Simulate 空 plan 期望返回空列表");
    }
}
