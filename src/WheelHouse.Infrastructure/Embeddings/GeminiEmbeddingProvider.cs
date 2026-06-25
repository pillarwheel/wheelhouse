using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure.Agents;

namespace WheelHouse.Infrastructure.Embeddings;

/// <summary>Cloud embedding backend: delegates to Gemini's embedding model.</summary>
public class GeminiEmbeddingProvider : IEmbeddingProvider
{
    private readonly IGeminiService _gemini;
    private readonly GeminiOptions _options;

    public GeminiEmbeddingProvider(IGeminiService gemini, GeminiOptions options)
    {
        _gemini = gemini;
        _options = options;
    }

    public string Id => $"gemini-{_options.EmbeddingModel}";
    public int Dimensions => _options.EmbeddingDimensions;
    public bool IsAvailable => _gemini.IsConfigured;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        => _gemini.EmbedAsync(text, cancellationToken);
}
