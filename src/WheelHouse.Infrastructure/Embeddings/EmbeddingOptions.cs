namespace WheelHouse.Infrastructure.Embeddings;

/// <summary>Selects and configures the active embedding backend.</summary>
public class EmbeddingOptions
{
    /// <summary>"auto" (prefer local, fall back to Gemini), "local", or "gemini".</summary>
    public string Backend { get; set; } = "auto";

    /// <summary>
    /// Directory containing <c>model.onnx</c> and <c>vocab.txt</c> for the local model.
    /// Defaults to %LOCALAPPDATA%\WheelHouse\models\all-MiniLM-L6-v2.
    /// </summary>
    public string LocalModelDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WheelHouse", "models", "all-MiniLM-L6-v2");

    /// <summary>Max tokens fed to the local model (MiniLM supports up to 256/512).</summary>
    public int MaxTokens { get; set; } = 256;
}
