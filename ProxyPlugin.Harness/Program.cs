using System.Net;
using System.Net.WebSockets;
using System.Text;
using ProxyPlugin;
using ProxyPlugin.Core;

internal static class Program
{
    private static int _failures;

    private static async Task<int> Main()
    {
        const int originPort = 18080;
        var originPrefix = $"http://localhost:{originPort}/";

        using var origin = StartServer(originPrefix, _ => (200, "ORIGIN"));

        var settings = new ProxySettings { Mode = NetworkMode.NoProxy, Enabled = true };

        // Rule maps the origin host to a guaranteed-unresolvable host so the mirror leg always
        // fails, exercising the rewrite -> failure -> origin-fallback path end to end.
        var ruleset = new MirrorRuleset(new[]
        {
            new MirrorRule { Id = "t", Pattern = "^localhost", Replacement = "nonexistent.invalid", Enabled = true },
        });
        var health = new MirrorHealth(new SystemClock(), failureThreshold: 5, cooldown: TimeSpan.FromSeconds(2));
        var log = new RequestLog();
        var proxy = new DynamicProxy(() => settings, new NullSystemProxyReader());
        var interceptor = new HttpInterceptor(proxy, ruleset, health, log, () => settings, new ConsoleLog());

        interceptor.Install();
        try
        {
            // (A) The single most uncertain assumption: reflection into SocketsHttpHandler
            // internals resolves on the real .NET 10 runtime.
            Console.WriteLine($"ForceInjectionAvailable = {interceptor.ForceInjectionAvailable}");
            Check("A: forced-injection reflection resolved", interceptor.ForceInjectionAvailable);

            // (B) Rewrite -> mirror DNS failure -> transparent fallback to origin returns 200/ORIGIN.
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            {
                var body = await client.GetStringAsync($"http://localhost:{originPort}/ok");
                Check("B: rewrite->mirror-fail->origin-fallback returns origin body", body == "ORIGIN");
            }

            // (C) Forced proxy injection: an opt-out (UseProxy=false) handler must still be routed
            // through the (dead) upstream proxy. Verified differentially against a connectivity
            // control so an offline machine yields SKIP, not a false failure.
            settings.Mode = NetworkMode.NoProxy;
            bool online;
            using (var h = new SocketsHttpHandler { UseProxy = false })
            using (var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(8) })
            {
                try { (await c.GetAsync("http://example.com/")).Dispose(); online = true; }
                catch { online = false; }
            }

            if (!online)
            {
                Console.WriteLine("[SKIP] C: forced injection (no internet connectivity)");
            }
            else
            {
                settings.Mode = NetworkMode.ManualProxy;
                settings.ManualProxyUrl = "http://127.0.0.1:9"; // discard port, nothing listening
                bool threw = false;
                using (var h2 = new SocketsHttpHandler { UseProxy = false })
                using (var c2 = new HttpClient(h2) { Timeout = TimeSpan.FromSeconds(8) })
                {
                    try { (await c2.GetAsync("http://example.com/")).Dispose(); }
                    catch { threw = true; }
                }
                Check("C: forced injection routes opt-out handler through dead proxy", threw);
                settings.Mode = NetworkMode.NoProxy;
                settings.ManualProxyUrl = null;
            }

            // (D) WebSocket handshake funnels through SocketsHttpHandler.SendAsync (and thus our
            // patch) without crashing. Host 127.0.0.1 deliberately does not match the rule above.
            using (var ws = new ClientWebSocket())
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await ws.ConnectAsync(new Uri("ws://127.0.0.1:1/"), cts.Token); }
                catch { /* expected: connection refused */ }
            }
            var sawWs = log.Snapshot().Any(e => e.Host == "127.0.0.1");
            Check("D: websocket handshake passed through interceptor (logged)", sawWs);
        }
        finally
        {
            interceptor.Dispose();
        }

        Console.WriteLine(_failures == 0
            ? "ALL HARNESS CHECKS PASSED"
            : $"{_failures} HARNESS CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok)
    {
        Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}");
        if (!ok)
            _failures++;
    }

    private static HttpListener StartServer(string prefix, Func<HttpListenerContext, (int status, string body)> handler)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                try
                {
                    var (status, body) = handler(ctx);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = status;
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
                catch
                {
                    try { ctx.Response.Abort(); } catch { /* ignore */ }
                }
            }
        });
        return listener;
    }

    private sealed class ConsoleLog : IInterceptorLog
    {
        public void Error(Exception ex, string message) => Console.WriteLine($"[ERR] {message}: {ex.Message}");
        public void Info(string message) => Console.WriteLine($"[INF] {message}");
    }

    private sealed class NullSystemProxyReader : ISystemProxyReader
    {
        public IWebProxy? GetCurrentProxy() => null;
    }
}
