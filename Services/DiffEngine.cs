using System;
using System.Collections.Generic;

namespace MyIDE.Services;

/// <summary>
/// 简易行级差异引擎：用 LCS 算法求最长公共子序列，输出每行的状态
/// </summary>
public class DiffEngine
{
    /// <summary>单行的差异状态</summary>
    public enum LineKind { Same, Added, Removed }

    public class DiffLine
    {
        public LineKind Kind { get; set; }
        public string Text { get; set; } = "";
        /// <summary>在原始（before）中的行号，从 1 开始；不适用则为 0</summary>
        public int OldLineNo { get; set; }
        /// <summary>在新（after）中的行号，从 1 开始；不适用则为 0</summary>
        public int NewLineNo { get; set; }
    }

    /// <summary>
    /// 计算两组行的对齐结果。返回的序列里同时包含 before 和 after 的行（Removed 来自 before，Added 来自 after，Same 两边都有）。
    /// </summary>
    public static List<DiffLine> Diff(string[] before, string[] after)
    {
        var result = new List<DiffLine>();
        int n = before.Length, m = after.Length;

        // dp[i, j] = before[0..i) 与 after[0..j) 的 LCS 长度
        var dp = new int[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                dp[i, j] = before[i - 1] == after[j - 1]
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        // 反向回溯
        int bi = n, bj = m;
        var stack = new Stack<DiffLine>();
        while (bi > 0 && bj > 0)
        {
            if (before[bi - 1] == after[bj - 1])
            {
                stack.Push(new DiffLine { Kind = LineKind.Same, Text = before[bi - 1], OldLineNo = bi, NewLineNo = bj });
                bi--; bj--;
            }
            else if (dp[bi - 1, bj] >= dp[bi, bj - 1])
            {
                stack.Push(new DiffLine { Kind = LineKind.Removed, Text = before[bi - 1], OldLineNo = bi, NewLineNo = 0 });
                bi--;
            }
            else
            {
                stack.Push(new DiffLine { Kind = LineKind.Added, Text = after[bj - 1], OldLineNo = 0, NewLineNo = bj });
                bj--;
            }
        }
        while (bi > 0)
        {
            stack.Push(new DiffLine { Kind = LineKind.Removed, Text = before[bi - 1], OldLineNo = bi, NewLineNo = 0 });
            bi--;
        }
        while (bj > 0)
        {
            stack.Push(new DiffLine { Kind = LineKind.Added, Text = after[bj - 1], OldLineNo = 0, NewLineNo = bj });
            bj--;
        }

        while (stack.Count > 0) result.Add(stack.Pop());
        return result;
    }

    /// <summary>汇总统计：新增/删除/相同 行数</summary>
    public static (int added, int removed, int same) Summarize(List<DiffLine> diff)
    {
        int a = 0, r = 0, s = 0;
        foreach (var l in diff)
        {
            if (l.Kind == LineKind.Added) a++;
            else if (l.Kind == LineKind.Removed) r++;
            else s++;
        }
        return (a, r, s);
    }
}
