using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartMemReduct
{
    // ── Provider presets ──

    public class AiProvider
    {
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string[] SuggestedModels { get; set; } = Array.Empty<string>();
        public string DefaultModel { get; set; } = "";
        /// <summary>Extra headers needed for this provider (e.g. HTTP-Referer for OpenRouter).</summary>
        public Dictionary<string, string>? ExtraHeaders { get; set; }
    }

    // ── Process snapshot for AI analysis ──

    public class ProcessSnapshot
    {
        public string Name { get; set; } = "";
        public int Pid { get; set; }
        public double WorkingSetMB { get; set; }
        public double PrivateBytesMB { get; set; }
    }

    // ── AI Optimizer engine ──

    public static class AiOptimizer
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // ── Known providers ──

        public static readonly AiProvider[] KnownProviders = new[]
        {
            new AiProvider
            {
                Name = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                SuggestedModels = new[] { "gpt-4.1-nano", "gpt-4.1-mini", "gpt-4o-mini", "gpt-4o", "gpt-4.1" },
                DefaultModel = "gpt-4.1-nano"
            },
            new AiProvider
            {
                Name = "OpenRouter",
                BaseUrl = "https://openrouter.ai/api/v1",
                SuggestedModels = new[] {
                    "google/gemini-2.5-flash",
                    "anthropic/claude-sonnet-4",
                    "openai/gpt-4.1-nano",
                    "meta-llama/llama-4-maverick",
                    "deepseek/deepseek-chat-v3-0324",
                    "qwen/qwen3-32b"
                },
                DefaultModel = "google/gemini-2.5-flash",
                ExtraHeaders = new Dictionary<string, string>
                {
                    ["HTTP-Referer"] = "https://smart-skiimer.app",
                    ["X-Title"] = "Smart Skii-mer"
                }
            },
            new AiProvider
            {
                Name = "Groq",
                BaseUrl = "https://api.groq.com/openai/v1",
                SuggestedModels = new[] { "llama-3.3-70b-versatile", "llama-3.1-8b-instant", "mixtral-8x7b-32768", "gemma2-9b-it" },
                DefaultModel = "llama-3.3-70b-versatile"
            },
            new AiProvider
            {
                Name = "Together AI",
                BaseUrl = "https://api.together.xyz/v1",
                SuggestedModels = new[] { "meta-llama/Llama-3.3-70B-Instruct-Turbo", "mistralai/Mixtral-8x7B-Instruct-v0.1", "Qwen/Qwen2.5-72B-Instruct-Turbo" },
                DefaultModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo"
            },
            new AiProvider
            {
                Name = "NVIDIA NIM",
                BaseUrl = "https://integrate.api.nvidia.com/v1",
                SuggestedModels = new[] { "nvidia/llama-3.3-nemotron-super-49b-v1", "deepseek-ai/deepseek-r1", "qwen/qwen3-235b-a22b" },
                DefaultModel = "nvidia/llama-3.3-nemotron-super-49b-v1"
            },
            new AiProvider
            {
                Name = "Local (LM Studio / Ollama)",
                BaseUrl = "http://localhost:1234/v1",
                SuggestedModels = new[] { "local-model" },
                DefaultModel = "local-model"
            },
            new AiProvider
            {
                Name = "Custom",
                BaseUrl = "",
                SuggestedModels = Array.Empty<string>(),
                DefaultModel = ""
            }
        };

        // ── Gather system state for AI ──

        public static List<ProcessSnapshot> GetTopProcesses(int count = 15)
        {
            var snapshots = new List<ProcessSnapshot>();
            try
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        snapshots.Add(new ProcessSnapshot
                        {
                            Name = p.ProcessName,
                            Pid = p.Id,
                            WorkingSetMB = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 1),
                            PrivateBytesMB = Math.Round(p.PrivateMemorySize64 / (1024.0 * 1024.0), 1)
                        });
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            return snapshots
                .OrderByDescending(s => s.WorkingSetMB)
                .Take(count)
                .ToList();
        }

        private static string BuildSystemPrompt()
        {
            return @"You are an expert Windows system memory optimizer embedded in Smart Skii-mer, a system tray app.
Your job is to analyze the system's current memory state and running processes, then provide:
1. A brief health assessment (1-2 sentences)
2. The top memory concerns (which processes are bloated or leaking)
3. Actionable recommendations (keep it practical — what the user should do)

Rules:
- Be concise. Max 8-10 lines.
- Use plain language, not jargon.
- Never recommend killing system processes (dwm, csrss, explorer, lsass, svchost, etc.)
- If memory looks healthy, say so — don't invent problems.
- If you see patterns suggesting a memory leak (a process using way more than expected), call it out.
- Mention if the user should run a Deep Clean (Ctrl+Alt+D) or if Light auto-cleaning is enough.";
        }

        private static string BuildAnalysisMessage(MemoryInfo mem, List<ProcessSnapshot> topProcs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Current System Memory State ===");
            sb.AppendLine($"Physical RAM: {mem.UsedPhysicalGB} GB / {mem.TotalPhysicalGB} GB ({mem.MemoryLoadPercent}% used)");
            sb.AppendLine($"Free RAM: {mem.AvailablePhysicalGB} GB");
            sb.AppendLine($"Commit Charge: {mem.UsedCommitGB} GB / {mem.TotalCommitGB} GB ({mem.CommitLoadPercent}%)");
            sb.AppendLine();
            sb.AppendLine("=== Top Processes by Memory (Working Set) ===");
            foreach (var p in topProcs)
            {
                sb.AppendLine($"  {p.Name} (PID {p.Pid}): {p.WorkingSetMB} MB working set, {p.PrivateBytesMB} MB private");
            }
            sb.AppendLine();
            sb.AppendLine("Analyze this system state and give your assessment.");
            return sb.ToString();
        }

        // ── Call AI API ──

        /// <summary>
        /// Sends a chat completion request to any OpenAI-compatible API.
        /// This works with OpenAI, OpenRouter, Groq, Together, NVIDIA NIM,
        /// LM Studio, Ollama, and any other provider that uses the same format.
        /// </summary>
        public static async Task<string> CallChatApiAsync(
            string baseUrl, string apiKey, string model,
            string systemPrompt, string userMessage,
            Dictionary<string, string>? extraHeaders = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
                return "[Error] API URL and Key are required. Configure them in Settings → AI Optimizer.";

            string url = baseUrl.TrimEnd('/') + "/chat/completions";

            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                },
                max_tokens = 500,
                temperature = 0.3
            };

            string json = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            if (extraHeaders != null)
            {
                foreach (var kv in extraHeaders)
                {
                    request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }

            try
            {
                var response = await _httpClient.SendAsync(request, ct);
                string responseJson = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    return $"[API Error {(int)response.StatusCode}] {TruncateError(responseJson)}";
                }

                // Parse the response — we just need choices[0].message.content
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? "[Empty response]";
                    }
                }

                return "[Error] Could not parse AI response. Raw: " + TruncateError(responseJson);
            }
            catch (TaskCanceledException)
            {
                return "[Cancelled] The request was cancelled.";
            }
            catch (HttpRequestException ex)
            {
                return $"[Connection Error] {ex.Message}\nCheck your API URL and internet connection.";
            }
            catch (Exception ex)
            {
                return $"[Error] {ex.Message}";
            }
        }

        // ── High-level analyze ──

        /// <summary>
        /// Performs a full AI-powered system memory analysis using the configured BYOK settings.
        /// </summary>
        public static async Task<string> AnalyzeSystemAsync(CancellationToken ct = default)
        {
            var settings = SettingsManager.Current.Ai;
            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiKey))
                return "[AI Optimizer is not configured]\n\nGo to Settings → AI Optimizer tab to enter your API key.\nSupports: OpenAI, OpenRouter, Groq, Together AI, NVIDIA NIM, and any OpenAI-compatible API.";

            var mem = MemoryEngine.GetCurrentMemoryInfo();
            var topProcs = GetTopProcesses(15);

            string systemPrompt = BuildSystemPrompt();
            string userMessage = BuildAnalysisMessage(mem, topProcs);

            // Find extra headers if using a known provider
            Dictionary<string, string>? headers = null;
            foreach (var p in KnownProviders)
            {
                if (string.Equals(p.Name, settings.ProviderName, StringComparison.OrdinalIgnoreCase))
                {
                    headers = p.ExtraHeaders;
                    break;
                }
            }

            return await CallChatApiAsync(
                settings.BaseUrl, settings.ApiKey, settings.ModelName,
                systemPrompt, userMessage, headers, ct);
        }

        /// <summary>
        /// Interactive chat — user asks a question about their system and the AI responds
        /// with context about the current memory state.
        /// </summary>
        public static async Task<string> ChatAsync(string userQuestion, CancellationToken ct = default)
        {
            var settings = SettingsManager.Current.Ai;
            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiKey))
                return "[AI not configured] Set up your API key in Settings → AI Optimizer.";

            var mem = MemoryEngine.GetCurrentMemoryInfo();
            var topProcs = GetTopProcesses(10);

            string systemPrompt = BuildSystemPrompt() +
                "\n\nThe user is asking a follow-up question. Answer using the system context provided.";

            string contextAndQuestion = BuildAnalysisMessage(mem, topProcs) +
                $"\n\n=== User Question ===\n{userQuestion}";

            Dictionary<string, string>? headers = null;
            foreach (var p in KnownProviders)
            {
                if (string.Equals(p.Name, settings.ProviderName, StringComparison.OrdinalIgnoreCase))
                {
                    headers = p.ExtraHeaders;
                    break;
                }
            }

            return await CallChatApiAsync(
                settings.BaseUrl, settings.ApiKey, settings.ModelName,
                systemPrompt, contextAndQuestion, headers, ct);
        }

        /// <summary>
        /// Quick connection test — sends a tiny request to verify the API key works.
        /// </summary>
        public static async Task<(bool success, string message)> TestConnectionAsync(
            string baseUrl, string apiKey, string model,
            Dictionary<string, string>? extraHeaders = null,
            CancellationToken ct = default)
        {
            try
            {
                string result = await CallChatApiAsync(
                    baseUrl, apiKey, model,
                    "You are a connection test. Respond with exactly: OK",
                    "Test. Respond with one word: OK",
                    extraHeaders, ct);

                if (result.StartsWith("["))
                    return (false, result);

                return (true, $"Connected! Model '{model}' responded: {TruncateError(result, 80)}");
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}");
            }
        }

        private static string TruncateError(string s, int max = 300)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }
    }
}
