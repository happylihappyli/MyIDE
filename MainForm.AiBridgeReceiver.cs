using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyIDE;

public partial class MainForm
{
    private const string AiReplyImportPrefix = "http://127.0.0.1:18889/";
    private HttpListener? _aiReplyImportListener;
    private CancellationTokenSource? _aiReplyImportListenerCts;
    private string _lastImportedAiReplySignature = "";

    /// <summary>
    /// MyChrome 推送最新 AI 回复时使用的请求结构。
    /// </summary>
    private sealed class AiReplyImportRequest
    {
        public string ReplyText { get; set; } = "";
        public string Source { get; set; } = "";
        public string Url { get; set; } = "";
        public string Signature { get; set; } = "";
        public string CopyMethod { get; set; } = "";
        public string CopyButtonHint { get; set; } = "";
        public string DebugInfo { get; set; } = "";
        public bool AutoDetected { get; set; }
    }

    /// <summary>
    /// 启动本地接收服务，让 MyChrome 在检测到回复完成后可直接推送到 MyIDE。
    /// </summary>
    private void StartAiReplyImportServer()
    {
        if (_aiReplyImportListener != null) return;

        try
        {
            _aiReplyImportListenerCts = new CancellationTokenSource();
            _aiReplyImportListener = new HttpListener();
            _aiReplyImportListener.Prefixes.Add(AiReplyImportPrefix);
            _aiReplyImportListener.Start();
            Log("✔ 已启动 AI 回复接收服务：127.0.0.1:18889");
            _ = Task.Run(() => AiReplyImportLoopAsync(_aiReplyImportListenerCts.Token));
        }
        catch (Exception ex)
        {
            Warn("启动 AI 回复接收服务失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 停止本地接收服务，释放监听端口。
    /// </summary>
    private void StopAiReplyImportServer()
    {
        try
        {
            _aiReplyImportListenerCts?.Cancel();
            if (_aiReplyImportListener?.IsListening == true)
            {
                _aiReplyImportListener.Stop();
            }
            _aiReplyImportListener?.Close();
        }
        catch
        {
        }
        finally
        {
            _aiReplyImportListener = null;
            _aiReplyImportListenerCts?.Dispose();
            _aiReplyImportListenerCts = null;
        }
    }

    /// <summary>
    /// 持续监听 MyChrome 发来的 AI 回复推送。
    /// </summary>
    private async Task AiReplyImportLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested &&
               _aiReplyImportListener != null &&
               _aiReplyImportListener.IsListening)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _aiReplyImportListener.GetContextAsync();
                _ = Task.Run(() => HandleAiReplyImportRequestAsync(context), cancellationToken);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                if (context != null)
                {
                    try { context.Response.StatusCode = 500; context.Response.Close(); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// 处理单次 AI 回复导入请求，并把内容安全回填到编辑区。
    /// </summary>
    private async Task HandleAiReplyImportRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";

        try
        {
            if (context.Request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var requestPath = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (context.Request.HttpMethod != "POST" || requestPath != "/api/ai/import-reply")
            {
                response.StatusCode = 404;
                await WriteAiReplyImportResponseAsync(response, new { ok = false, message = "not_found" });
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<AiReplyImportRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (request == null || string.IsNullOrWhiteSpace(request.ReplyText))
            {
                response.StatusCode = 400;
                await WriteAiReplyImportResponseAsync(response, new { ok = false, message = "reply_required" });
                return;
            }

            var importResult = await InvokeAsync(() => ImportAiReplyFromBridge(request));
            response.StatusCode = importResult ? 200 : 409;
            await WriteAiReplyImportResponseAsync(response, new { ok = importResult, message = importResult ? "imported" : "duplicate" });
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            await WriteAiReplyImportResponseAsync(response, new { ok = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 把桥接收到的 AI 回复导入当前编辑区，并更新历史与状态。
    /// </summary>
    private bool ImportAiReplyFromBridge(AiReplyImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ReplyText)) return false;

        var signature = string.IsNullOrWhiteSpace(request.Signature)
            ? request.ReplyText.Trim()
            : request.Signature.Trim();
        if (string.Equals(signature, _lastImportedAiReplySignature, StringComparison.Ordinal))
        {
            return false;
        }

        var importedText = request.ReplyText;
        var normalizedByRepair = false;
        try
        {
            var extracted = ExtractAiResponseBody(request.ReplyText);
            importedText = NormalizeAiResponseBody(request.ReplyText);
            normalizedByRepair = !string.Equals(extracted, importedText, StringComparison.Ordinal);
        }
        catch
        {
            importedText = request.ReplyText;
        }

        _txtAi.Text = importedText;
        _txtAi.SelectionStart = _txtAi.TextLength;
        _txtAi.SelectionLength = 0;
        UpdateAiJsonBufferStatus();
        TrySaveCurrentAiJsonToSidebar();
        _lastImportedAiReplySignature = signature;

        _lblStatus.Text = "● 已自动接收 AI 返回内容";
        _lblStatus.ForeColor = Success;
        _lblAiBrowser.Text = request.AutoDetected ? "AI 浏览器: 已自动回传" : "AI 浏览器: 已回传";
        _lblAiBrowser.ForeColor = Success;

        Log($"✔ 已接收 MyChrome 回传的 AI 回复：{BuildShortPreview(request.Source)} {BuildShortPreview(request.Url, 48)}");
        Log($"· 回传方式：{(request.AutoDetected ? "自动检测完成" : "手动触发")} | 复制方式：{request.CopyMethod}");
        if (normalizedByRepair)
        {
            Log("· 回传内容已做最小规范化修复。");
        }
        if (!string.IsNullOrWhiteSpace(request.DebugInfo))
        {
            Log($"· MyChrome 调试信息：{request.DebugInfo}");
        }

        ShowTransientStatus("● 已自动接收 AI 返回内容", Success);
        return true;
    }

    /// <summary>
    /// 把导入接口的响应写回给 MyChrome。
    /// </summary>
    private static async Task WriteAiReplyImportResponseAsync(HttpListenerResponse response, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
    }

    /// <summary>
    /// 在 UI 线程执行委托，并把结果异步返回给后台监听线程。
    /// </summary>
    private Task<T> InvokeAsync<T>(Func<T> action)
    {
        if (!InvokeRequired) return Task.FromResult(action());

        var tcs = new TaskCompletionSource<T>();
        BeginInvoke(new Action(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }));
        return tcs.Task;
    }
}
