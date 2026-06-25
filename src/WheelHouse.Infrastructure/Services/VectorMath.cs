namespace WheelHouse.Infrastructure.Services;

/// <summary>Small numeric helpers for local vector search.</summary>
public static class VectorMath
{
    /// <summary>Cosine similarity in [-1, 1]. Returns 0 for empty/mismatched vectors.</summary>
    public static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count == 0 || b.Count == 0 || a.Count != b.Count) return 0d;

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * (double)a[i];
            magB += b[i] * (double)b[i];
        }
        if (magA == 0 || magB == 0) return 0d;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
