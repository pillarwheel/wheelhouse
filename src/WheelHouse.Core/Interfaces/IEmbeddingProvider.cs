namespace WheelHouse.Core.Interfaces;

/// <summary>
/// Produces embedding vectors for text. Implementations may be local (on-device ONNX)
/// or cloud (Gemini). The active provider is chosen at startup based on availability.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Stable identifier of the model/backend, e.g. "local-all-MiniLM-L6-v2".</summary>
    string Id { get; }

    /// <summary>Dimensionality of the vectors this provider emits.</summary>
    int Dimensions { get; }

    /// <summary>True when this provider can actually run (model present / key configured).</summary>
    bool IsAvailable { get; }

    /// <summary>Embeds a single text. Returns an empty array when unavailable.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
