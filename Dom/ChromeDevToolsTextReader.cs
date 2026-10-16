using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AutoInputTester.Dom;

/// <summary>
/// Reads page text via Chrome/Edge DevTools Protocol (remote debugging).
/// Requires the browser started with --remote-debugging-port=9222
/// (see starten-chrome-typewriter.bat).
/// </summary>
public sealed class ChromeDevToolsTextReader
{
    private static readonly int[] Ports = { 9222, 9223, 9224, 9333 };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1.5) };

    public async Task<string?> TryReadTypewriterRemainingAsync(CancellationToken token)
    {
        foreach (var port in Ports)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var targets = await ListTargetsAsync(port, token).ConfigureAwait(false);
                if (targets.Count == 0) continue;

                var preferred = PreferTypewriterTab(targets);
                foreach (var target in preferred)
                {
                    token.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
                        continue;

                    var text = await EvaluateAsync(
                        target.WebSocketDebuggerUrl,
                        TypewriterDomScript.ExtractRemainingText,
                        token).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(text) && text.Length >= 1)
                        return text;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Try next port / target.
            }
        }

        return null;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken token)
    {
        foreach (var port in Ports)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var targets = await ListTargetsAsync(port, token).ConfigureAwait(false);
                if (targets.Count > 0) return true;
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        return false;
    }

    private static async Task<List<CdpTarget>> ListTargetsAsync(int port, CancellationToken token)
    {
        var url = $"http://127.0.0.1:{port}/json";
        using var response = await Http.GetAsync(url, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var list = await JsonSerializer.DeserializeAsync<List<CdpTarget>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            token).ConfigureAwait(false);
        return list ?? new List<CdpTarget>();
    }

    private static IEnumerable<CdpTarget> PreferTypewriterTab(List<CdpTarget> targets)
    {
        static int Score(CdpTarget t)
        {
            var u = (t.Url ?? string.Empty).ToLowerInvariant();
            var title = (t.Title ?? string.Empty).ToLowerInvariant();
            var score = 0;
            // Prefer Typewriter EDU (edu.typewriter.at) over at4/legacy.
            if (u.Contains("edu.typewriter.at")) score += 200;
            if (u.Contains("runexercise") || u.Contains("typewriter/run")) score += 80;
            if (u.Contains("typewriter")) score += 100;
            if (title.Contains("typewriter")) score += 50;
            if (u.Contains("tippen") || u.Contains("schreib")) score += 20;
            if (string.Equals(t.Type, "page", StringComparison.OrdinalIgnoreCase)) score += 5;
            return score;
        }

        return targets
            .Where(t => string.Equals(t.Type, "page", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrEmpty(t.Type))
            .OrderByDescending(Score);
    }

    private static async Task<string?> EvaluateAsync(string wsUrl, string expression, CancellationToken token)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(wsUrl), token).ConfigureAwait(false);

        var id = 1;
        var payload = JsonSerializer.Serialize(new
        {
            id,
            method = "Runtime.evaluate",
            @params = new
            {
                expression,
                returnByValue = true,
                awaitPromise = false
            }
        });

        var bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, token).ConfigureAwait(false);

        var buffer = new byte[256 * 1024];
        var deadline = Environment.TickCount64 + 4000;

        while (Environment.TickCount64 < deadline)
        {
            token.ThrowIfCancellationRequested();
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, token).ConfigureAwait(false);
                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) break;
                if (ms.Length > 2_000_000) break;
            }

            var json = Encoding.UTF8.GetString(ms.ToArray());
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("id", out var idNode) || idNode.GetInt32() != id)
                continue; // CDP event noise

            if (!doc.RootElement.TryGetProperty("result", out var resultNode))
                break;
            if (!resultNode.TryGetProperty("result", out var inner))
                break;

            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false); }
            catch { }

            if (inner.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();

            return null;
        }

        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "timeout", CancellationToken.None).ConfigureAwait(false); }
        catch { }
        return null;
    }

    private sealed class CdpTarget
    {
        public string? Description { get; set; }
        public string? DevtoolsFrontendUrl { get; set; }
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Type { get; set; }
        public string? Url { get; set; }
        public string? WebSocketDebuggerUrl { get; set; }
    }
}
