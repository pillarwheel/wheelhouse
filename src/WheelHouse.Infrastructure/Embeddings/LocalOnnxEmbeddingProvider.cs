using FastBertTokenizer;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WheelHouse.Core.Interfaces;

namespace WheelHouse.Infrastructure.Embeddings;

/// <summary>
/// Fully on-device embedding backend running all-MiniLM-L6-v2 via ONNX Runtime.
/// Produces 384-dim sentence embeddings (mean-pooled + L2-normalized), so the entire
/// RAG path can run offline with no network calls.
/// </summary>
public sealed class LocalOnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly EmbeddingOptions _options;
    private readonly ILogger<LocalOnnxEmbeddingProvider> _logger;
    private readonly object _gate = new();

    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private bool _initFailed;

    public LocalOnnxEmbeddingProvider(EmbeddingOptions options, ILogger<LocalOnnxEmbeddingProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string Id => "local-all-MiniLM-L6-v2";
    public int Dimensions => 384;

    private string ModelPath => Path.Combine(_options.LocalModelDirectory, "model.onnx");
    private string VocabPath => Path.Combine(_options.LocalModelDirectory, "vocab.txt");

    public bool IsAvailable => !_initFailed && File.Exists(ModelPath) && File.Exists(VocabPath);

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return Array.Empty<float>();
        if (!EnsureInitialized()) return Array.Empty<float>();

        var (ids, mask, types) = _tokenizer!.Encode(text ?? string.Empty, _options.MaxTokens);
        var length = ids.Length;

        var idTensor = new DenseTensor<long>(ids.ToArray(), new[] { 1, length });
        var maskTensor = new DenseTensor<long>(mask.ToArray(), new[] { 1, length });
        var typeTensor = new DenseTensor<long>(types.ToArray(), new[] { 1, length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", idTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", typeTensor)
        };

        return await Task.Run(() =>
        {
            using var results = _session!.Run(inputs);
            // last_hidden_state: [1, seq, 384]
            var hidden = results.First().AsTensor<float>();
            return MeanPoolAndNormalize(hidden, mask.Span, length);
        }, cancellationToken);
    }

    private static float[] MeanPoolAndNormalize(Tensor<float> hidden, ReadOnlySpan<long> mask, int seqLen)
    {
        var dim = hidden.Dimensions[2];
        var pooled = new float[dim];
        long counted = 0;

        for (var t = 0; t < seqLen; t++)
        {
            if (mask[t] == 0) continue;
            counted++;
            for (var d = 0; d < dim; d++)
                pooled[d] += hidden[0, t, d];
        }

        if (counted == 0) counted = 1;
        double norm = 0;
        for (var d = 0; d < dim; d++)
        {
            pooled[d] /= counted;
            norm += pooled[d] * (double)pooled[d];
        }

        norm = Math.Sqrt(norm);
        if (norm > 0)
            for (var d = 0; d < dim; d++)
                pooled[d] = (float)(pooled[d] / norm);

        return pooled;
    }

    private bool EnsureInitialized()
    {
        if (_session is not null && _tokenizer is not null) return true;
        if (_initFailed) return false;

        lock (_gate)
        {
            if (_session is not null && _tokenizer is not null) return true;
            if (_initFailed) return false;
            try
            {
                var tokenizer = new BertTokenizer();
                using (var vocab = File.OpenText(VocabPath))
                    tokenizer.LoadVocabulary(vocab, convertInputToLowercase: true);

                _session = new InferenceSession(ModelPath);
                _tokenizer = tokenizer;
                _logger.LogInformation("Loaded local embedding model from {Dir}", _options.LocalModelDirectory);
                return true;
            }
            catch (Exception ex)
            {
                _initFailed = true;
                _logger.LogError(ex, "Failed to initialize local ONNX embedding model.");
                return false;
            }
        }
    }

    public void Dispose() => _session?.Dispose();
}
