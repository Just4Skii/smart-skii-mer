using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartMemReduct
{
    /// <summary>
    /// Local-network control server for the companion phone app.
    /// - Loopback + private LAN addresses only.
    /// - Pairing with a short-lived 4-digit code, then a bearer token.
    /// - Fixed command allow-list; no arbitrary input reaches the OS.
    /// </summary>
    public sealed class RemoteServer : IDisposable
    {
        public const int Port = 47821;

        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<string, DateTime> _tokens = new();
        private readonly Action<string> _onCommand;
        private readonly Func<string> _statusProvider;
        private readonly CancellationTokenSource _cts = new();
        private int _failures;
        private DateTime _lockedUntil = DateTime.MinValue;

        public string PairCode { get; private set; } = "";
        public int Attempts { get; private set; }

        private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "quick_clean", "deep_clean", "free_background", "status",
            "mode_game", "mode_stream", "mode_work", "mode_eco", "mode_balance", "mode_auto",
        };

        public RemoteServer(Action<string> onCommand, Func<string> statusProvider)
        {
            _onCommand = onCommand;
            _statusProvider = statusProvider;
            RotateCode();
        }

        public void RotateCode()
        {
            PairCode = Random.Shared.Next(1000, 10000).ToString();
            Attempts = 0;
        }

        public void Start()
        {
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://+:{Port}/");
                _listener.Start();
                _ = Task.Run(AcceptLoop);
                LanAvailable = true;
                LastError = "";
                return;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            // Fallback: loopback only (no URL ACL / firewall permission). The PC screen
            // still works; the phone needs `Smart Skii-mer --allow-lan` once.
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _ = Task.Run(AcceptLoop);
                LanAvailable = false;
            }
            catch (Exception loopEx)
            {
                LastError += " | loopback: " + loopEx.Message;
            }
        }

        public bool LanAvailable { get; private set; } = true;
        public string LastError { get; private set; } = "";
        public bool Running => _listener.IsListening;

        public static IReadOnlyList<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        list.Add(ip);
                    }
                }
            }
            catch { }
            return list;
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch { break; }

                _ = Task.Run(async () =>
                {
                    try { await HandleAsync(ctx).ConfigureAwait(false); }
                    catch { try { ctx.Response.Abort(); } catch { } }
                });
            }
        }

        private static bool IsPrivate(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
        }

        private bool IsAuthorized(HttpListenerRequest req)
        {
            var header = req.Headers["Authorization"];
            if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
            var token = header[7..].Trim();
            if (!_tokens.TryGetValue(token, out var expiry)) return false;
            if (expiry < DateTime.UtcNow) { _tokens.TryRemove(token, out _); return false; }
            return true;
        }

        private static void Write(HttpListenerContext ctx, int code, string body, string? contentType = "application/json")
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = code;
                if (contentType is not null) ctx.Response.ContentType = contentType + "; charset=utf-8";
                ctx.Response.Headers["Cache-Control"] = "no-store";
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
                ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
                ctx.Response.Headers["X-Frame-Options"] = "DENY";
                ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'unsafe-inline' 'self'; script-src 'unsafe-inline' 'self'; connect-src 'self'; frame-ancestors 'none'";
                // The companion page is same-origin; a local browser page may call it from its own origin.
                var origin = ctx.Request.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin) && (origin.StartsWith("http://localhost") || origin.StartsWith("http://127.0.0.1")))
                    ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                ctx.Response.Headers["Vary"] = "Origin";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var remote = req.RemoteEndPoint?.Address;

            if (remote is null || !IsPrivate(remote))
            {
                Write(ctx, 403, "{\"error\":\"local network only\"}");
                return;
            }

            var path = req.Url?.AbsolutePath ?? "/";

            if (req.HttpMethod == "OPTIONS") { Write(ctx, 204, "", null); return; }

            // ── static companion app ──
            if (req.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
            {
                Write(ctx, 200, MobileApp.Html, "text/html");
                return;
            }

            if (req.HttpMethod == "POST" && path == "/pair")
            {
                if (DateTime.UtcNow < _lockedUntil)
                {
                    Write(ctx, 429, "{\"error\":\"too many attempts\"}");
                    return;
                }

                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                string code = "";
                try { using var d = JsonDocument.Parse(body); code = d.RootElement.GetProperty("code").GetString() ?? ""; }
                catch { Write(ctx, 400, "{\"error\":\"bad request\"}"); return; }

                if (!string.Equals(code, PairCode, StringComparison.Ordinal))
                {
                    Attempts++;
                    if (Attempts >= 5)
                    {
                        _lockedUntil = DateTime.UtcNow.AddMinutes(2);
                        RotateCode();
                    }
                    Write(ctx, 401, "{\"error\":\"wrong code\"}");
                    return;
                }

                var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                _tokens[token] = DateTime.UtcNow.AddHours(12);
                RotateCode();
                Write(ctx, 200, JsonSerializer.Serialize(new { token, expires = "12h" }));
                return;
            }

            if (!IsAuthorized(req))
            {
                Write(ctx, 401, "{\"error\":\"unauthorized\"}");
                return;
            }

            if (req.HttpMethod == "GET" && path == "/status")
            {
                Write(ctx, 200, _statusProvider());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/command")
            {
                using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                string cmd = "";
                try { using var d = JsonDocument.Parse(body); cmd = d.RootElement.GetProperty("cmd").GetString() ?? ""; }
                catch { Write(ctx, 400, "{\"error\":\"bad request\"}"); return; }

                if (!AllowedCommands.Contains(cmd) || cmd.Length > 32)
                {
                    Write(ctx, 400, "{\"error\":\"unknown command\"}");
                    return;
                }

                _onCommand(cmd);
                Write(ctx, 200, "{\"ok\":true}");
                return;
            }

            Write(ctx, 404, "{\"error\":\"not found\"}");
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }
}
