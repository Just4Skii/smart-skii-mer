using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartMemReduct
{
    public sealed class AiSuggestion
    {
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Impact { get; set; } = "";
        public string Risk { get; set; } = "low";
        public string Action { get; set; } = "none";
    }

    public sealed class AiReport
    {
        public bool Ok { get; set; }
        public string Summary { get; set; } = "";
        public string Health { get; set; } = "";
        public List<AiSuggestion> Suggestions { get; } = new();
        public string Error { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string Raw { get; set; } = "";
    }

    /// <summary>
    /// BYOK analysis: OpenRouter first, NVIDIA NIM as fallback, keys resolved from
    /// settings → environment → PowerShell $PROFILE. Returns recommendations only;
    /// it never executes system changes.
    /// </summary>
    public static class AiAdvisor
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private static string _lastPayload = "";
        private static AiReport? _lastReport;
        private static DateTime _lastAt;

        
        /// <summary>Free OpenRouter models, cheapest/fastest first. Verified to return usable JSON.</summary>
        public static readonly string[] FreeModels =
        {
            "nvidia/nemotron-3.5-lightning:free",
            "dots-studio/dots-3-note-preview:free",
            "nvidia/nemotron-3-ultra-550b-a55b:free",
            "xingchen-agi/xing4.0-29b-a4b:free",
        };

        public static string? LastProvider { get; private set; }

        public static async Task<AiReport> AnalyzeAsync(SystemSnapshot snapshot, AiSettings settings, string modeName = "Balanced", CancellationToken ct = default)
        {
            var payload = SystemProbe.ToAiJson(snapshot).TrimEnd('}') + ",\"mode\":\"" + modeName + "\"}";
            if (_lastReport is not null && _lastPayload == payload && (DateTime.UtcNow - _lastAt).TotalMinutes < 10)
                return _lastReport;

            var failures = new List<string>();
            var (kind, baseUrl, model) = AiProviders.Resolve(settings);

            // 1) The user's configured provider (any OpenAI-compatible API or Gemini).
            if (kind != "auto")
            {
                var r = await AiProviders.CallAsync(settings, SystemPrompt(), "CURRENT SYSTEM SNAPSHOT:\n" + payload, 700, ct);
                if (r.Ok)
                {
                    var report = new AiReport { Ok = true, Raw = r.Content, Provider = r.Provider, Model = r.Model };
                    ParseReport(r.Content, report);
                    if (!report.Ok) report.Ok = true;
                    return Remember(report, $"{r.Provider} ({r.Model})", payload);
                }
                failures.Add($"{r.Provider}: {r.Error}");
            }

            // 2) Auto mode: free OpenRouter models, then NVIDIA.
            if (kind == "auto" || failures.Count > 0)
            {
                var orKey = ProfileKeys.FindOpenRouter(LocalKeyStore.LoadProvider() ?? settings.ApiKey);
                if (orKey is not null)
                {
                    foreach (var freeModel in FreeModels)
                    {
                        var fr = await CallAsync("https://openrouter.ai/api/v1/chat/completions", orKey.Key, freeModel, payload, 700, false, ct);
                        if (fr.Ok) return Remember(fr, "OpenRouter free (" + freeModel + ")", payload);
                        failures.Add(freeModel + ": " + fr.Error);
                    }
                }

                var nv = ProfileKeys.FindNvidia(null);
                if (nv is not null)
                {
                    using var nimCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    nimCts.CancelAfter(TimeSpan.FromSeconds(180));
                    var nr = await CallAsync("https://integrate.api.nvidia.com/v1/chat/completions", nv.Key, "minimaxai/minimax-m3.1-flash", payload, 500, true, nimCts.Token);
                    if (nr.Ok) return Remember(nr, "NVIDIA NIM", payload);
                    failures.Add("NVIDIA: " + nr.Error);
                }
            }

            var local = LocalAdvisor.Analyze(snapshot, string.Join(" | ", failures.Take(3)));
            local.Error = string.Join(" | ", failures.Take(3));
            return local;
        }

        private static AiReport Remember(AiReport r, string provider, string payload)
        {
            r.Provider = provider;
            LastProvider = provider;
            _lastPayload = payload;
            _lastReport = r;
            _lastAt = DateTime.UtcNow;
            return r;
        }

        /// <summary>Thin adapter so the automatic free/NVIDIA chain can share one parser.</summary>
        private static async Task<AiReport> CallAsync(
            string url, string key, string model, string payload, int maxTokens, bool nimFree, CancellationToken ct)
        {
            var referer = nimFree ? null : "https://graffgrid.co.za";
            var report = new AiReport { Model = model };
            try
            {
                var body = new
                {
                    model,
                    temperature = 0.2,
                    max_tokens = maxTokens,
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPrompt() },
                        new { role = "user", content = "CURRENT SYSTEM SNAPSHOT:\n" + payload },
                    },
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                if (referer is not null)
                {
                    req.Headers.TryAddWithoutValidation("HTTP-Referer", referer);
                    req.Headers.TryAddWithoutValidation("X-Title", "Smart Skii-mer");
                }
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    report.Error = $"{(int)res.StatusCode}: {(text.Length > 160 ? text[..160] + "…" : text)}";
                    return report;
                }

                using var doc = JsonDocument.Parse(text);
                var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                report.Raw = content;
                ParseReport(content, report);
                report.Ok = true;
                return report;
            }
            catch (Exception ex)
            {
                report.Error = ex.Message;
                return report;
            }
        }

        private static void ParseReport(string content, AiReport report)
        {
            try
            {
                using var d = JsonDocument.Parse(ExtractJson(content));
                var root = d.RootElement;
                report.Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                report.Health = root.TryGetProperty("health", out var h) ? h.GetString() ?? "" : "";
                if (root.TryGetProperty("suggestions", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        report.Suggestions.Add(new AiSuggestion
                        {
                            Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                            Detail = item.TryGetProperty("detail", out var de) ? de.GetString() ?? "" : "",
                            Impact = item.TryGetProperty("impact", out var im) ? im.GetString() ?? "" : "",
                            Risk = item.TryGetProperty("risk", out var ri) ? ri.GetString() ?? "low" : "low",
                            Action = item.TryGetProperty("action", out var ac) ? ac.GetString() ?? "none" : "none",
                        });
                    }
                }
            }
            catch
            {
                var m = System.Text.RegularExpressions.Regex.Match(content, "\\\"summary\\\"\\s*:\\s*\\\"(.*?)\\\"");
                if (m.Success) report.Summary = m.Groups[1].Value;
                var h = System.Text.RegularExpressions.Regex.Match(content, "\\\"health\\\"\\s*:\\s*\\\"(.*?)\\\"");
                if (h.Success) report.Health = h.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(report.Summary)) report.Summary = content.Trim();
            }
        }

        private static string ExtractJson(string s)
        {
            var start = s.IndexOf('{');
            var end = s.LastIndexOf('}');
            return start >= 0 && end > start ? s[start..(end + 1)] : "{}";
        }

        private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";

        private static string SystemPrompt() => """
Windows memory advisor. JSON only:
{"summary":"2 sentences","health":"healthy|strained|critical","suggestions":[{"title":"","detail":"","impact":"","risk":"low","action":"trim_background|open_graphics_settings|open_power_settings|open_vendor_app|none"}]}
Max 2 suggestions, detail 12 words. Never kill system/foreground apps. Recommend vendor profile for ASUS/MSI/Omen. Never edit drivers directly.
""";
    }
}
