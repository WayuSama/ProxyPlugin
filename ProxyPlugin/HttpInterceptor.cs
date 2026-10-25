using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using ProxyPlugin.Core;

namespace ProxyPlugin;

/// <summary>
/// Single interception point for the process-wide .NET HTTP/WS stack. Patches
/// <c>SocketsHttpHandler.SendAsync</c> / <c>Send</c> via Harmony to:
///  1. force every handler (even <c>UseProxy=false</c> ones) through <see cref="DynamicProxy"/>;
///  2. rewrite bodyless requests to mirror hosts in the allow-list; and
///  3. transparently fall back to the origin when a mirror request fails.
/// All interception logic is exception-isolated: a failure here never breaks the caller's request.
/// </summary>
public sealed class HttpInterceptor : IDisposable
{
    // Marks the rewritten origin so the fallback path can restore it.
    private static readonly HttpRequestOptionsKey<Uri> OriginalUriKey = new("ProxyPlugin.OriginalUri");

    // Marks fallback retries and self-issued requests (e.g. the UI proxy test) so we never
    // rewrite, inject, wrap, or log them again. Shared with callers that issue hands-off probes.
    internal static readonly HttpRequestOptionsKey<bool> NoRewriteKey = new("ProxyPlugin.NoRewrite");

    private static volatile HttpInterceptor? _active;

    private readonly HarmonyRuntime _harmony;
    private readonly DynamicProxy _proxy;
    private readonly MirrorRuleset _ruleset;
    private readonly MirrorHealth _health;
    private readonly RequestLog _log;
    private readonly Func<ProxySettings> _settingsProvider;
    private readonly IInterceptorLog _logger;
    private readonly HttpClient _fallbackClient;
    private int _fallbackInFlight;

    private PropertyInfo? _settingsProp;
    private FieldInfo? _proxyField;
    private FieldInfo? _useProxyField;
    private FieldInfo? _handlerField;

    public bool ForceInjectionAvailable { get; private set; }

    public HttpInterceptor(
        DynamicProxy proxy,
        MirrorRuleset ruleset,
        MirrorHealth health,
        RequestLog log,
        Func<ProxySettings> settingsProvider,
        IInterceptorLog logger, HarmonyRuntime harmony)
    {
        _harmony = harmony;
        _proxy = proxy;
        _ruleset = ruleset;
        _health = health;
        _log = log;
        _settingsProvider = settingsProvider;
        _logger = logger;

        // Owned client used to re-issue requests against the origin on mirror failure.
        // It carries the same DynamicProxy so it honors the current mode (direct in NoProxy,
        // upstream in proxy modes) and is exempted from rewrite via NoRewriteKey.
        _fallbackClient = new HttpClient(
            new SocketsHttpHandler { UseProxy = true, Proxy = _proxy },
            disposeHandler: true);
    }

    public void Install()
    {
        // MonoMod emits helper assemblies. Resolve their Harmony types in the same
        // non-collectible context as Harmony, not the caller's plugin context.
        using var scope = _harmony.EnterContext();
        try
        {
            InstallPatches();
        }
        catch
        {
            _active = null;
            _harmony.Unpatch();
            throw;
        }
    }

    private void InstallPatches()
    {
        ResolveReflection();
        _active = this;

        var sendAsync = DeclaredMethod(
            typeof(SocketsHttpHandler), "SendAsync",
            new[] { typeof(HttpRequestMessage), typeof(CancellationToken) });
        var send = DeclaredMethod(
            typeof(SocketsHttpHandler), "Send",
            new[] { typeof(HttpRequestMessage), typeof(CancellationToken) });

        if (sendAsync != null)
        {
            _harmony.Patch(sendAsync,
                prefix: PatchMethod(nameof(SendAsyncPrefix)),
                postfix: PatchMethod(nameof(SendAsyncPostfix)));
        }
        else
        {
            _logger.Info("ProxyPlugin: SocketsHttpHandler.SendAsync not found; async interception disabled.");
        }

        // Sync path gets forced-proxy injection only; rewrite/fallback stays async-only so a
        // mirror failure on a sync request can never become an un-recoverable error.
        if (send != null)
        {
            _harmony.Patch(send,
                prefix: PatchMethod(nameof(SendSyncPrefix)));
        }

        // GetAsync/PostAsync/SendAsync all hand this overload's task straight back to the caller.
        // We observe its faults (see HttpClientSendAsyncPostfix) so a fire-and-forget network error
        // is recorded in our request log instead of flooding the shared Dalamud log.
        var httpClientSendAsync = DeclaredMethod(
            typeof(HttpClient), "SendAsync",
            new[] { typeof(HttpRequestMessage), typeof(HttpCompletionOption), typeof(CancellationToken) });
        if (httpClientSendAsync != null)
        {
            _harmony.Patch(httpClientSendAsync,
                postfix: PatchMethod(nameof(HttpClientSendAsyncPostfix)));
        }
        else
        {
            _logger.Info("ProxyPlugin: HttpClient.SendAsync not found; unobserved-fault suppression disabled.");
        }

        if (!ForceInjectionAvailable)
            _logger.Info("ProxyPlugin: forced-proxy reflection unavailable on this runtime; running in degraded (baseline + rewrite) mode.");
    }

    private static MethodInfo? DeclaredMethod(Type type, string name, Type[] arguments)
        => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, arguments, null);

    private static MethodInfo PatchMethod(string name) => typeof(HttpInterceptor).GetMethod(name)!;

    private void ResolveReflection()
    {
        try
        {
            var handlerType = typeof(SocketsHttpHandler);
            _settingsProp = handlerType.GetProperty("Settings", BindingFlags.NonPublic | BindingFlags.Instance);
            _handlerField = handlerType.GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_settingsProp != null)
            {
                var settingsType = _settingsProp.PropertyType;
                _proxyField = settingsType.GetField("_proxy", BindingFlags.NonPublic | BindingFlags.Instance);
                _useProxyField = settingsType.GetField("_useProxy", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            ForceInjectionAvailable =
                _settingsProp != null && _proxyField != null && _useProxyField != null && _handlerField != null;
        }
        catch (Exception ex)
        {
            ForceInjectionAvailable = false;
            _logger.Error(ex, "ProxyPlugin: reflection probe for SocketsHttpHandler internals failed.");
        }
    }

    // ---- Harmony patch entry points (static; route to the active instance) ----

    public static void SendAsyncPrefix(object __instance, HttpRequestMessage request)
    {
        var self = _active;
        if (self == null)
            return;
        try { self.PreProcess(__instance, request, allowRewrite: true); }
        catch (Exception ex) { self._logger.Error(ex, "ProxyPlugin: SendAsync prefix failed."); }
    }

    public static void SendSyncPrefix(object __instance, HttpRequestMessage request)
    {
        var self = _active;
        if (self == null)
            return;
        try { self.PreProcess(__instance, request, allowRewrite: false); }
        catch (Exception ex) { self._logger.Error(ex, "ProxyPlugin: Send prefix failed."); }
    }

    public static void SendAsyncPostfix(HttpRequestMessage request, ref Task<HttpResponseMessage> __result)
    {
        var self = _active;
        if (self == null || __result == null)
            return;

        try
        {
            // Master switch off: do not wrap or log; leave the caller's task untouched.
            if (!self._settingsProvider().Enabled)
                return;

            // Do not wrap/log fallback retries or self-issued requests twice.
            if (IsBypassRequest(request))
                return;

            request.Options.TryGetValue(OriginalUriKey, out var originalUri);
            var inner = __result;
            __result = self.WrapAsync(request, originalUri, inner);
        }
        catch (Exception ex)
        {
            // Interception must never break the caller; leave __result unchanged.
            self._logger.Error(ex, "ProxyPlugin: SendAsync postfix failed.");
        }
    }

    // ---- Core logic ----

    private void PreProcess(object handler, HttpRequestMessage request, bool allowRewrite)
    {
        if (request.RequestUri == null)
            return;

        var settings = _settingsProvider();
        if (!settings.Enabled)
            return;

        // Leave self-issued requests (fallback retries, the UI proxy test) entirely alone:
        // no rewrite and no forced injection. The fallback client already carries DynamicProxy.
        if (IsBypassRequest(request))
            return;

        if (allowRewrite
            && request.Content == null
            && ShouldRewrite(settings)
            && _ruleset.TryRewrite(request.RequestUri, out var rewritten)
            && !_health.IsOpen(rewritten.Host))
        {
            request.Options.Set(OriginalUriKey, request.RequestUri);
            request.RequestUri = rewritten;
            request.Headers.Host = null; // re-derive Host from the mirror authority
        }

        TryInject(handler);
    }

    private static bool ShouldRewrite(ProxySettings settings)
        => settings.Mode == NetworkMode.NoProxy;

    private static bool IsBypassRequest(HttpRequestMessage request)
        => request.Options.TryGetValue(NoRewriteKey, out var skip) && skip;

    private void TryInject(object handler)
    {
        if (!ForceInjectionAvailable)
            return;

        // Only inject before the connection pool / handler chain is built; afterwards the
        // settings have already been cloned and our write would have no effect.
        if (_handlerField!.GetValue(handler) != null)
            return;

        var settings = _settingsProp!.GetValue(handler);
        if (settings == null)
            return;

        _useProxyField!.SetValue(settings, true);
        _proxyField!.SetValue(settings, _proxy);
    }

    private async Task<HttpResponseMessage> WrapAsync(
        HttpRequestMessage request, Uri? originalUri, Task<HttpResponseMessage> inner)
    {
        var sw = Stopwatch.StartNew();
        var rewritten = originalUri != null;
        var finalUri = request.RequestUri;
        var mirrorHost = finalUri?.Host ?? string.Empty;
        var proxied = IsProxied(finalUri);

        HttpResponseMessage? response = null;
        Exception? error = null;
        try { response = await inner.ConfigureAwait(false); }
        catch (Exception ex) { error = ex; }

        if (rewritten && FailureClassifier.ShouldFallback(response, error))
        {
            var clone = RequestCloner.TryClone(request, originalUri!);
            if (clone != null)
            {
                _health.ReportFailure(mirrorHost);
                response?.Dispose();
                clone.Options.Set(NoRewriteKey, true);
                sw.Restart();
                Interlocked.Increment(ref _fallbackInFlight);
                try
                {
                    var originResp = await _fallbackClient
                        .SendAsync(clone, HttpCompletionOption.ResponseHeadersRead)
                        .ConfigureAwait(false);
                    Log(originalUri!, originalUri!, false, proxied, (int)originResp.StatusCode, "fallback", sw.ElapsedMilliseconds, originResp.Version.ToString());
                    return originResp;
                }
                catch
                {
                    Log(originalUri!, originalUri!, false, proxied, 0, "fallback-failed", sw.ElapsedMilliseconds);
                    throw;
                }
                finally
                {
                    Interlocked.Decrement(ref _fallbackInFlight);
                }
            }
        }

        if (rewritten && response != null && !FailureClassifier.ShouldFallback(response, null))
            _health.ReportSuccess(mirrorHost);

        var final = finalUri ?? request.RequestUri!;
        var original = originalUri ?? final;
        var statusCode = response != null ? (int)response.StatusCode : 0;
        Log(original, final, rewritten, proxied, statusCode, error != null ? "failed" : "ok", sw.ElapsedMilliseconds, response?.Version.ToString() ?? "");

        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();

        return response!;
    }

    private bool IsProxied(Uri? uri)
    {
        if (uri == null)
            return false;
        try { return !_proxy.IsBypassed(uri); }
        catch { return false; }
    }

    private void Log(Uri original, Uri final, bool rewritten, bool proxied, int statusCode, string result, long elapsedMs, string httpVersion = "")
    {
        try
        {
            _log.Add(new RequestLogEntry
            {
                Host = final.Host,
                OriginalUrl = original.ToString(),
                FinalUrl = final.ToString(),
                Rewritten = rewritten,
                Proxied = proxied,
                StatusCode = statusCode,
                Result = result,
                ElapsedMs = elapsedMs,
                HttpVersion = httpVersion,
            });
        }
        catch
        {
            // diagnostics must never affect request flow
        }
    }

    // Postfix on HttpClient.SendAsync. A caller that fire-and-forgets a request never observes the
    // returned task, so when it faults (typically a network error: DNS/connect/timeout) the finalizer
    // escalates it to TaskScheduler.UnobservedTaskException, which Dalamud logs as a full-stack [ERR].
    // Dalamud subscribes to that event before any plugin and logs while the fault is still unobserved,
    // so a late SetObserved cannot stop it. Instead we attach our own observer here: observing the
    // fault defuses the escalation while leaving the exception intact for callers that do await it.
    public static void HttpClientSendAsyncPostfix(ref Task<HttpResponseMessage> __result)
    {
        var self = _active;
        if (self == null || __result == null)
            return;

        try
        {
            // Master switch off: leave the runtime's default reporting untouched.
            if (!self._settingsProvider().Enabled)
                return;

            ObserveFault(__result);
        }
        catch (Exception ex)
        {
            self._logger.Error(ex, "ProxyPlugin: HttpClient.SendAsync postfix failed.");
        }
    }

    // Reading Task.Exception marks the fault observed (so the finalizer never escalates it) without
    // clearing it, so an awaiting caller still sees the error. OnlyOnFaulted keeps the success path
    // free; the continuation itself completes normally and so is never unobserved in turn.
    private static void ObserveFault(Task task)
    {
        if (task.IsCompletedSuccessfully)
            return;

        task.ContinueWith(
            static t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        using var scope = _harmony.EnterContext();
        // Stop accepting new interception first so no new fallback can start.
        _active = null;
        try { _harmony.Unpatch(); }
        catch (Exception ex) { _logger.Error(ex, "ProxyPlugin: Harmony unpatch failed."); }

        // Best-effort drain of in-flight fallbacks so disposing the client does not surface
        // ObjectDisposedException into an unrelated caller's request.
        var deadline = Environment.TickCount64 + 1500;
        while (Volatile.Read(ref _fallbackInFlight) > 0 && Environment.TickCount64 < deadline)
            Thread.Sleep(10);

        _fallbackClient.Dispose();
    }
}
