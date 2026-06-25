using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;

namespace WheelHouse.Infrastructure.Agents;

/// <summary>
/// Gemini wrapper for research, task generation, troubleshooting and embeddings.
///
/// Implements two R&amp;D ideas from the plan:
///  - <b>KV-Cache Alignment</b>: stable system/context preamble is emitted first and verbatim,
///    so the provider can reuse cached prefix state across calls; only the volatile task text varies.
///  - <b>Context Compression</b>: callers pass source already shrunk via <see cref="ICodeCompressionService"/>.
/// </summary>
public class GeminiService : IGeminiService
{
    private const string StablePreamble =
        "You are the planning brain of WheelHouse, an autonomous coding-orchestration system. " +
        "You produce precise, build-ready engineering guidance for a downstream coding agent (Claude Code). " +
        "Always be concrete, reference file paths, and prefer verifiable steps.";

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(HttpClient http, GeminiOptions options, ILogger<GeminiService> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        => GenerateAsync(prompt, cancellationToken);

    public async Task<string> GenerateResearchPlanAsync(
        string goal, string repositoryContext, CancellationToken cancellationToken = default)
    {
        var prompt =
            $"## Repository context\n{repositoryContext}\n\n" +
            $"## Goal\n{goal}\n\n" +
            "Produce a concise markdown implementation plan: numbered steps, files to touch, and risks.";
        return await GenerateAsync(prompt, cancellationToken);
    }

    public async Task<IReadOnlyList<TaskItem>> GenerateTasksAsync(
        string plan, CancellationToken cancellationToken = default)
    {
        var prompt =
            "Convert the following plan into a JSON array of tasks. " +
            "Each item: {\"title\":string,\"description\":string,\"verificationCommand\":string|null}. " +
            "Return ONLY the JSON array.\n\n## Plan\n" + plan;

        var raw = await GenerateAsync(prompt, cancellationToken, jsonMode: true);
        return ParseTasks(raw);
    }

    public Task<string> TroubleshootAsync(
        string command, string output, string repositoryContext, CancellationToken cancellationToken = default)
    {
        var prompt =
            $"## Repository context\n{repositoryContext}\n\n" +
            $"## Failing command\n`{command}`\n\n## Output\n```\n{output}\n```\n\n" +
            "Diagnose the root cause and give the exact fix (commands and/or code edits).";
        return GenerateAsync(prompt, cancellationToken);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return Array.Empty<float>();

        var url = $"{_options.BaseUrl}/models/{_options.EmbeddingModel}:embedContent?key={_options.ApiKey}";
        var body = new
        {
            model = $"models/{_options.EmbeddingModel}",
            content = new { parts = new[] { new { text } } },
            outputDimensionality = _options.EmbeddingDimensions
        };

        try
        {
            using var resp = await PostWithRetryAsync(url, body, cancellationToken);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
            var values = doc.RootElement.GetProperty("embedding").GetProperty("values");
            var vec = new float[values.GetArrayLength()];
            var i = 0;
            foreach (var v in values.EnumerateArray()) vec[i++] = v.GetSingle();
            return vec;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding request failed.");
            return Array.Empty<float>();
        }
    }

    private async Task<string> GenerateAsync(
        string userPrompt, CancellationToken cancellationToken, bool jsonMode = false)
    {
        if (!IsConfigured)
            return "_Gemini is not configured (set GEMINI_API_KEY). Returning placeholder plan._";

        var url = $"{_options.BaseUrl}/models/{_options.GenerationModel}:generateContent?key={_options.ApiKey}";
        object body = jsonMode
            ? new
            {
                system_instruction = new { parts = new[] { new { text = StablePreamble } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
                // Force structured JSON output so task parsing is reliable.
                generationConfig = new { responseMimeType = "application/json" }
            }
            : new
            {
                // Stable system instruction first → KV-cache friendly prefix.
                system_instruction = new { parts = new[] { new { text = StablePreamble } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } }
            };

        try
        {
            using var resp = await PostWithRetryAsync(url, body, cancellationToken);
            var payload = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini error {Status}: {Body}", resp.StatusCode, payload);
                return $"_Gemini request failed ({(int)resp.StatusCode})._";
            }

            using var doc = JsonDocument.Parse(payload);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
            return text ?? "(empty response)";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini generation failed.");
            return $"_Gemini request errored: {ex.Message}._";
        }
    }

    private static readonly HttpStatusCode[] TransientCodes =
    {
        HttpStatusCode.TooManyRequests,        // 429
        HttpStatusCode.InternalServerError,    // 500
        HttpStatusCode.BadGateway,             // 502
        HttpStatusCode.ServiceUnavailable,     // 503 (Gemini "model overloaded")
        HttpStatusCode.GatewayTimeout          // 504
    };

    /// <summary>POSTs JSON with exponential-backoff retries on transient (429/5xx) responses.</summary>
    private async Task<HttpResponseMessage> PostWithRetryAsync(
        string url, object body, CancellationToken cancellationToken, int maxAttempts = 4)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response = await _http.PostAsJsonAsync(url, body, cancellationToken);
            if (response.IsSuccessStatusCode ||
                !TransientCodes.Contains(response.StatusCode) ||
                attempt == maxAttempts)
                return response;

            var delayMs = 400 * (int)Math.Pow(2, attempt - 1); // 400, 800, 1600ms
            _logger.LogWarning("Gemini {Status}; retry {Attempt}/{Max} in {Delay}ms",
                (int)response.StatusCode, attempt, maxAttempts, delayMs);
            response.Dispose();
            await Task.Delay(delayMs, cancellationToken);
        }
        return response!;
    }

    private static IReadOnlyList<TaskItem> ParseTasks(string raw)
    {
        var json = ExtractJsonArray(raw);
        if (json is null) return Array.Empty<TaskItem>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<TaskItem>();
            var seq = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                list.Add(new TaskItem
                {
                    Sequence = seq++,
                    Title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Description = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    VerificationCommand = el.TryGetProperty("verificationCommand", out var v) &&
                                          v.ValueKind == JsonValueKind.String
                        ? v.GetString()
                        : null
                });
            }
            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<TaskItem>();
        }
    }

    /// <summary>Extracts the first top-level JSON array from a (possibly fenced) string.</summary>
    private static string? ExtractJsonArray(string raw)
    {
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        return start >= 0 && end > start ? raw.Substring(start, end - start + 1) : null;
    }
}
