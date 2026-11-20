using Microsoft.Extensions.AI;

namespace ShopAssist.Core.Knowledge;

public sealed record SearchHit(PolicyChunk Chunk, float Score);

/// <summary>
/// A small in-memory vector index. For a few dozen chunks, a linear scan is fast enough.
/// Real systems use a vector database (Day 16 discusses options).
/// </summary>
public class PolicyIndex(IEmbeddingGenerator<string, Embedding<float>> embedder)
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder = embedder;
    private readonly List<(PolicyChunk Chunk, ReadOnlyMemory<float> Vector)> _items = [];
    public int Count => _items.Count;

    public async Task BuildAsync(IEnumerable<PolicyChunk> chunks, CancellationToken ct = default)
    {
        var list = chunks.ToList();

        // The text we embed includes the title and section, so the meaning is grounded.
        var inputs = list.Select(c => $"{c.Title} - {c.Section}: {c.Text}").ToList();

        var vectors = await _embedder.GenerateAsync(inputs, cancellationToken: ct);

        for (int i = 0; i < list.Count; i++)
            _items.Add((list[i], vectors[i].Vector));
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int topK = 3, CancellationToken ct = default)
    {
        var q = await _embedder.GenerateAsync([query], cancellationToken: ct);

        var queryVector = q[0].Vector;

        return _items
            .Select(item => new SearchHit(item.Chunk, Cosine(queryVector.Span, item.Vector.Span)))
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();
    }

    // Cosine similarity: the angle between two vectors. 1 = same direction, 0 = unrelated.
    private static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB) + 1e-9f);
    }
}
