using static System.Collections.Specialized.BitVector32;

namespace ShopAssist.Core.Knowledge;

public sealed record PolicyChunk(string Id, string Title, string Section, string Text);

/// <summary>
/// Splits a markdown policy document into one chunk per "## section".
/// Each chunk carries its document title and section heading, so it makes sense on its own.
/// </summary>
public static class PolicyChunker
{
    public static IReadOnlyList<PolicyChunk> Chunk(string markdown)
    {
        var chunks  = new List<PolicyChunk>();

        string docTitle = "Untitled";

        string? sectionTitle = null;

        var body = new List<string>();


        void Flush()
        {
            if (sectionTitle is null || body.Count == 0) return;
            var text = string.Join(" ", body.Where(l => l.Trim().Length > 0)).Trim();
            if (text.Length == 0) return;

            var id = $"{Slug(docTitle)}-{Slug(sectionTitle)}";
            chunks.Add(new PolicyChunk(id, docTitle, sectionTitle, text));
            body.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.StartsWith("# "))
            {
                Flush();
                docTitle = line[2..].Trim();
                sectionTitle = null;
            }
            else if (line.StartsWith("## "))
            {
                Flush();
                sectionTitle = line[3..].Trim();
            }
            else if (sectionTitle is not null)
            {
                body.Add(line);
            }
        }

        Flush();

        return chunks;
    }

    private static string Slug(string s) => 
        new string(s.ToLowerInvariant()
                    .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                    .ToArray())
                    .Trim('-');
}
