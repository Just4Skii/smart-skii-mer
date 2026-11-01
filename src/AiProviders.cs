using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartMemReduct
{
    public sealed class ProviderResult
    {
        public bool Ok { get; init; }
        public string Content { get; init; } = "";
        public string Error { get; init; } = "";
        public string Provider { get; init; } = "";
        public string Model { get; init; } = "";
    }

    /// <summary>
    /// One adapter for every provider:
    ///  - any OpenAI-compatible /chat/completions endpoint (DeepSeek, OpenAI, Groq,
    ///    Together, Mistral, local Ollama / LM Studio, ...)
    ///  - Google Gemini's generateContent format
    /// Keys are supplied per provider and are never logged.
    /// </summary>
    public static class AiProviders
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

        public sealed record Preset(string Name, string Kind, string BaseUrl, string DefaultModel, string KeyHint);

        public static readonly Preset[] Presets =
        {
            new("Auto (free models first)", "auto", "", "nvidia/nemotron-3.5-lightning:free", "sk-or-..."),
            new("OpenRouter", "openrouter", "https://openrouter.ai/api/v1", "google/gemini-2.5-flash", "sk-or-..."),
            new("DeepSeek", "compat", "https://api.deepseek.com/v1", "deepseek-flash", "sk-..."),
            new("OpenAI", "compat", "https://api.openai.com/v1", "gpt-4.1-mini", "sk-..."),
            new("Groq", "compat", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", "gsk_..."),
            new("Mistral", "compat", "https://api.mistral.ai/v1", "mistral-small-latest", "…"),
            new("Together AI", "compat", "https://api.together.xyz/v1", "meta-llama/Llama-3.3-70B-Instruct-Turbo", "…"),
            new("NVIDIA NIM", "nvidia", "https://integrate.api.nvidia.com/v1", "minimaxai/minimax-m3.1-flash", "nvapi-..."),
            new("Google Gemini", "gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.0-flash", "AIza..."),
            new("Local (Ollama / LM Studio)", "compat", "http://localhost:11434/v1", "llama3.1", "(no key)"),
            new("Custom OpenAI-compatible", "compat", "", "", "…"),
        };

        public static Preset? PresetFor(string kindOrName) =>
            Presets.FirstOrDefault(p =>
                p.Kind.Equals(kindOrName, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(kindOrName, StringComparison.OrdinalIgnoreCase));

        /// <summary>Resolves the endpoint/model/kind the user configured.</summary>
        public static (string kind, string baseUrl, string model) Resolve(AiSettings s)
        {
            var preset = PresetFor(s.Kind) ?? PresetFor(s.ProviderName) ?? Presets[0];
            var baseUrl = !string.IsNullOrWhiteSpace(s.CustomBaseUrl)
                ? s.CustomBaseUrl.Trim()
                : !string.IsNullOrWhiteSpace(s.BaseUrl) && preset.Kind == "compat" && s.Kind != "compat"
                    ? s.BaseUrl.Trim()
                    : preset.BaseUrl;
            if (preset.Kind == "compat" && string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(s.BaseUrl))
                baseUrl = s.BaseUrl.Trim();
            var model = string.IsNullOrWhiteSpace(s.ModelName) ? preset.DefaultModel : s.ModelName.Trim();
            return (preset.Kind, baseUrl.TrimEnd('/'), model);
        }

        public static async Task<ProviderResult> CallAsync(
            AiSettings settings, string systemPrompt, string userContent, int maxTokens, CancellationToken ct)
        {
            var (kind, baseUrl, model) = Resolve(settings);
            var key = LocalKeyStore.LoadProvider();
            if (string.IsNullOrWhiteSpace(key)) key = settings.ApiKey;
            var providerLabel = (PresetFor(settings.Kind) ?? Presets[0]).Name;

            if (kind == "gemini")
            {
                return await CallGeminiAsync(baseUrl, model, key, systemPrompt, userContent, maxTokens, ct);
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
                return new ProviderResult { Error = "No endpoint configured", Provider = providerLabel };

            return await CallOpenAiCompatibleAsync(baseUrl, model, key, systemPrompt, userContent, maxTokens, ct, providerLabel);
        }

        private static bool IsLocal(string baseUrl) =>
            baseUrl.Contains("localhost") || baseUrl.Contains("127.0.0.1");

        private static async Task<ProviderResult> CallOpenAiCompatibleAsync(
            string baseUrl, string model, string? key, string systemPrompt, string userContent,
            int maxTokens, CancellationToken ct, string provider)
        {
            try
            {
                var body = new
                {
                    model,
                    temperature = 0.2,
                    max_tokens = maxTokens,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent },
                    },
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");
                if (!string.IsNullOrWhiteSpace(key))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                if (baseUrl.Contains("openrouter.ai"))
                {
                    req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://graffgrid.co.za");
                    req.Headers.TryAddWithoutValidation("X-Title", "Smart Skii-mer");
                }
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!res.IsSuccessStatusCode)
                {
                    var hint = (int)res.StatusCode switch
                    {
                        401 => "key rejected",
                        402 => "no credit on this account",
                        403 => "access denied for this model",
                        404 => "endpoint or model not found",
                        429 => "rate limited — try again shortly",
                        _ => $"HTTP {(int)res.StatusCode}",
                    };
                    return new ProviderResult { Provider = provider, Model = model, Error = $"{hint}: {Trim(text, 160)}" };
                }

                using var doc = JsonDocument.Parse(text);
                var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                return new ProviderResult { Ok = true, Content = content, Provider = provider, Model = model };
            }
            catch (TaskCanceledException)
            {
                return new ProviderResult { Provider = provider, Model = model, Error = "timed out" };
            }
            catch (Exception ex)
            {
                return new ProviderResult { Provider = provider, Model = model, Error = ex.Message };
            }
        }

        private static async Task<ProviderResult> CallGeminiAsync(
            string baseUrl, string model, string? key, string systemPrompt, string userContent,
            int maxTokens, CancellationToken ct)
        {
            try
            {
                var url = $"{baseUrl}/models/{Uri.EscapeDataString(model)}:generateContent";
                var body = new
                {
                    system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                    contents = new[] { new { role = "user", parts = new[] { new { text = userContent } } } },
                    generationConfig = new
                    {
                        temperature = 0.2,
                        maxOutputTokens = maxTokens,
                        responseMimeType = "application/json",
                    },
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("x-goog-api-key", key ?? "");
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                    return new ProviderResult { Provider = "Google Gemini", Model = model, Error = $"HTTP {(int)res.StatusCode}: {Trim(text, 160)}" };

                using var doc = JsonDocument.Parse(text);
                var parts = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts");
                var sb = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                    if (part.TryGetProperty("text", out var t)) sb.Append(t.GetString());
                return new ProviderResult { Ok = true, Content = sb.ToString(), Provider = "Google Gemini", Model = model };
            }
            catch (Exception ex)
            {
                return new ProviderResult { Provider = "Google Gemini", Model = model, Error = ex.Message };
            }
        }

        public static bool NeedsKey(string kind) =>
            !Presets.Any(p => p.Kind == "compat" && p.Name.StartsWith("Local") && p.Name.Equals(kind, StringComparison.OrdinalIgnoreCase));

        private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
    }
}
