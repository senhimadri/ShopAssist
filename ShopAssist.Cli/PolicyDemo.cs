using Microsoft.Extensions.AI;
using ShopAssist.Core.Knowledge;

namespace ShopAssist.Cli;

public static class PolicyDemo
{
    public static async Task RunAsync(IEmbeddingGenerator<string, Embedding<float>> embedder, string policyPath)
    {
        var markdown = await File.ReadAllTextAsync(policyPath);
        var chunks = PolicyChunker.Chunk(markdown);

        Console.WriteLine($"Chunked policies into {chunks.Count} pieces:");

        foreach (var c in chunks)
            Console.WriteLine($"  - [{c.Id}] {c.Title} / {c.Section} ({c.Text.Length} chars)");

        var index = new PolicyIndex(embedder);

        await index.BuildAsync(chunks);
        Console.WriteLine($"Embedded {index.Count} chunks.\n");

        string[] questions =
        [
            "Can I send my tent back?",
            "How long do I have to get my money back?",
            "My stove arrived broken, what do I do?",
            "Is there a guarantee on the sleeping bag?",
            "How long does delivery take?",
        ];

        foreach (var q in questions)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Q: {q}");
            Console.ResetColor();

            foreach (var hit in await index.SearchAsync(q, topK: 2))
                Console.WriteLine($"   {hit.Score,6:F3}  {hit.Chunk.Title} / {hit.Chunk.Section}");
            Console.WriteLine();
        }
    }
}
