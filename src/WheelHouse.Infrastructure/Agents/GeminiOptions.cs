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
}
