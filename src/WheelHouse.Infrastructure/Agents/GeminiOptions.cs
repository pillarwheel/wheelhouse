namespace WheelHouse.Infrastructure.Agents;

/// <summary>Configuration for <see cref="GeminiService"/>.</summary>
public class GeminiOptions
{
    /// <summary>API key. Defaults to the <c>GEMINI_API_KEY</c> environment variable.</summary>
    public string? ApiKey { get; set; } = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>Model used for planning / research / troubleshooting.</summary>
    public string GenerationModel { get; set; } = "gemini-2.5-flash";

    /// <summary>Model used for code embeddings.</summary>
    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    /// <summary>Requested embedding dimensionality (gemini-embedding-001 supports 128–3072).</summary>
    public int EmbeddingDimensions { get; set; } = 768;

    /// <summary>
    /// "auto"/"on" use Gemini explicit context caching for large repository contexts (default);
    /// "off" always sends context inline.
    /// </summary>
    public string ContextCacheMode { get; set; } =
        Environment.GetEnvironmentVariable("WHEELHOUSE_GEMINI_CACHE") ?? "auto";

    public bool ContextCacheEnabled =>
        !string.Equals(ContextCacheMode, "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Minimum context size (chars) worth caching. The API enforces a minimum cacheable token
    /// count (~4096 tokens on 2.5 Flash ≈ 16k chars); below it, caching is refused anyway.
    /// </summary>
    public int ContextCacheMinChars { get; set; } = 16_000;

    /// <summary>Cache TTL — long enough for a plan→fix iteration loop, short enough to bound storage cost.</summary>
    public int ContextCacheTtlSeconds { get; set; } = 600;
}
