using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Azure.Cosmos;
using OpenAI.Embeddings;
using UglyToad.PdfPig;

namespace seed_data;

/// <summary>
/// Seeds the KnowledgeDocuments container from PDF files in the contoso-sharepoint folder.
/// Each PDF is read, its text is extracted and chunked, each chunk is embedded using
/// the Azure OpenAI embedding model, and the result is upserted into Cosmos DB.
/// </summary>
public static class SharePointSeeder
{
    private const string ContainerName = "KnowledgeDocuments";
    private const int MaxChunkTokens = 500;          // approximate token limit per chunk
    private const int MaxChunkChars = MaxChunkTokens * 4; // rough chars-to-tokens ratio

    public static async Task SeedAsync(
        Database database,
        EmbeddingClient embeddingClient,
        string sharePointFolder)
    {
        if (!Directory.Exists(sharePointFolder))
        {
            Console.WriteLine($"  SharePoint folder not found: {sharePointFolder}");
            return;
        }

        var container = database.GetContainer(ContainerName);

        var pdfFiles = Directory.GetFiles(sharePointFolder, "*.pdf", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.Combine("generate-pdfs", ""), StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        Console.WriteLine($"  Found {pdfFiles.Count} PDF files in contoso-sharepoint/\n");

        int totalChunks = 0;

        foreach (var pdfFile in pdfFiles)
        {
            var relativePath = Path.GetRelativePath(sharePointFolder, pdfFile).Replace('\\', '/');
            var fileName = Path.GetFileNameWithoutExtension(pdfFile);
            var category = GetCategory(relativePath);

            // 1. Extract text from PDF
            var fullText = ExtractTextFromPdf(pdfFile);

            if (string.IsNullOrWhiteSpace(fullText))
            {
                Console.WriteLine($"  ⚠ Skipping {relativePath} — no text content extracted");
                continue;
            }

            // 2. Chunk the text
            var chunks = ChunkText(fullText);

            // 3. Embed and upsert each chunk
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunkId = $"{fileName}-chunk-{i + 1}";
                var chunkText = chunks[i];

                // Generate embedding
                var embeddingResult = await embeddingClient.GenerateEmbeddingAsync(chunkText);
                var vector = embeddingResult.Value.ToFloats().ToArray();

                // Build document
                var doc = new Dictionary<string, object>
                {
                    ["id"] = chunkId,
                    ["title"] = FormatTitle(fileName),
                    ["category"] = category,
                    ["source_file"] = relativePath,
                    ["chunk_index"] = i,
                    ["total_chunks"] = chunks.Count,
                    ["content"] = chunkText,
                    ["content_vector"] = vector,
                };

                var json = JsonSerializer.Serialize(doc);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

                await container.UpsertItemStreamAsync(stream, new PartitionKey(chunkId));
                totalChunks++;
            }

            Console.WriteLine($"  ✓ {relativePath}: {chunks.Count} chunk(s) embedded and upserted");
        }

        Console.WriteLine($"\n  Total: {totalChunks} document chunks in {ContainerName}");
    }

    private static string ExtractTextFromPdf(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        var text = new System.Text.StringBuilder();

        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }

        return text.ToString().Trim();
    }

    /// <summary>
    /// Splits text into chunks of approximately MaxChunkChars characters,
    /// breaking at paragraph boundaries when possible.
    /// </summary>
    private static List<string> ChunkText(string text)
    {
        var chunks = new List<string>();

        // Split on double newlines (paragraph breaks)
        var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        var currentChunk = new System.Text.StringBuilder();

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // If adding this paragraph would exceed the limit, flush current chunk
            if (currentChunk.Length > 0 && currentChunk.Length + trimmed.Length + 2 > MaxChunkChars)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }

            // If a single paragraph exceeds the limit, split it by sentences
            if (trimmed.Length > MaxChunkChars)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                var sentences = trimmed.Split(new[] { ". ", ".\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var sentence in sentences)
                {
                    if (currentChunk.Length + sentence.Length + 2 > MaxChunkChars && currentChunk.Length > 0)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }
                    currentChunk.Append(sentence.Trim());
                    if (!sentence.EndsWith('.')) currentChunk.Append('.');
                    currentChunk.Append(' ');
                }
            }
            else
            {
                if (currentChunk.Length > 0) currentChunk.AppendLine();
                currentChunk.Append(trimmed);
            }
        }

        // Flush remaining
        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    private static string GetCategory(string relativePath)
    {
        var parts = relativePath.Split('/');
        return parts.Length > 1 ? parts[0].TrimEnd('s') : "general"; // "policies" → "policy"
    }

    private static string FormatTitle(string fileName)
    {
        // "data-overage-policy" → "Data Overage Policy"
        return string.Join(' ', fileName.Split('-')
            .Select(w => char.ToUpper(w[0]) + w[1..]));
    }
}
