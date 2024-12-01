using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DocumentFormat.OpenXml.EMMA;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory.Context;
using Microsoft.KernelMemory.MemoryStorage;
using Microsoft.KernelMemory.Search;

namespace KernelMemory.StructRAG;

#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

public sealed class StructRAGSearchCient : ISearchClient
{
    private readonly IMemoryDb _memoryDb;
    private readonly ITextGenerator _textGenerator;
    private readonly SearchClientConfig _config;
    private readonly ILogger<StructRAGSearchCient> _log;

    public StructRAGSearchCient(
        IMemoryDb memoryDb,
        ITextGenerator textGenerator,
        SearchClientConfig? config = null,
        ILoggerFactory? loggerFactory = null)
    {
        this._memoryDb = memoryDb;
        this._textGenerator = textGenerator;
        this._log = loggerFactory?.CreateLogger<StructRAGSearchCient>() ?? new NullLogger<StructRAGSearchCient>();

        this._config = config ?? new SearchClientConfig();
        this._config.Validate();
    }

    public async Task<MemoryAnswer> AskAsync(string index, string question, ICollection<MemoryFilter>? filters = null, double minRelevance = 0, IContext? context = null, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("Asking question: {0}", question);

        var records = await GetSimilarRecordsAsync(index, question, filters, minRelevance, cancellationToken)
                                      .ConfigureAwait(false);

        if (_log.IsEnabled(LogLevel.Debug))
            _log.LogDebug("Found {0} relevant memories, maxRelevance: {1}, minRelevance: {2}", records.Count(), records.MaxBy(c => c.Relevance), records.MinBy(c => c.Relevance));

        if (!records.Any())
        {
            return new MemoryAnswer
            {
                Question = question,
                Result = this._config.EmptyAnswer
            };
        }

        var effectiveRecords = records.Select(c => c.Record);

        // 1. router
        var route = await Router(question, effectiveRecords, context, cancellationToken)
                            .ConfigureAwait(false);

        _log.LogInformation("Route: {0}", route);

        // 2. structurizer
        (var instruction, var info) = await ConstructAsync(route, question, effectiveRecords, context, cancellationToken)
                                            .ConfigureAwait(false);

        if (_log.IsEnabled(LogLevel.Trace))
            _log.LogTrace("Instruction: {0}\nInfo: {1}", instruction, info);

        // 3. utilizer
        var subqueries = await DecomposeAsync(question, info, context, cancellationToken)
                                        .ConfigureAwait(false);

        if (_log.IsEnabled(LogLevel.Trace))
            _log.LogTrace("Subqueries: {0}\n{1}", subqueries.Count(), string.Join(Environment.NewLine, subqueries));

        var subknowledges = await ExtractAsync(route, question, info, subqueries, context, cancellationToken)
                                        .ConfigureAwait(false);
        if (_log.IsEnabled(LogLevel.Trace))
            _log.LogTrace("Subknowledges: {0}\n{1}", subknowledges.Count(), string.Join(Environment.NewLine, subknowledges.Select(c => $"Subquery: {c.subquery}\nRetrieval results:\n{c.subknowledge}\n\n")));

        var answer = await MergeAsync(route, question, subknowledges, context, cancellationToken)
                                        .ConfigureAwait(false);

        return new MemoryAnswer()
        {
            Question = question,
            Result = answer,
            NoResult = false,
            RelevantSources = effectiveRecords
                    .GroupBy(c => c.GetDocumentId())
                    .Select(c => new Citation
                    {
                        DocumentId = c.Key,
                        FileId = c.First().GetFileId(),
                        Index = index,
                        Link = c.First().GetWebPageUrl(index),
                        Partitions = c.Select(p => new Citation.Partition
                        {
                            Text = p.GetPartitionText(),
                            LastUpdate = p.GetLastUpdate(),
                            PartitionNumber = p.GetPartitionNumber(),
                            SectionNumber = p.GetSectionNumber(),
                            Tags = p.Tags
                        }).ToList()
                    }).ToList()
        };
    }

    public async Task<IEnumerable<string>> ListIndexesAsync(CancellationToken cancellationToken = default)
    {
        return await this._memoryDb.GetIndexesAsync(cancellationToken)
                                   .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SearchResult> SearchAsync(
        string index,
        string query,
        ICollection<MemoryFilter>? filters = null,
        double minRelevance = 0,
        int limit = -1,
        IContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0) { limit = this._config.MaxMatchesCount; }

        var result = new SearchResult
        {
            Query = query,
            Results = []
        };

        if (string.IsNullOrWhiteSpace(query) && (filters == null || filters.Count == 0))
        {
            this._log.LogWarning("No query or filters provided");
            return result;
        }

        var list = new List<(MemoryRecord memory, double relevance)>();
        if (!string.IsNullOrEmpty(query))
        {
            this._log.LogTrace("Fetching relevant memories by similarity, min relevance {0}", minRelevance);
            IAsyncEnumerable<(MemoryRecord, double)> matches = this._memoryDb.GetSimilarListAsync(
                index: index,
                text: query,
                filters: filters,
                minRelevance: minRelevance,
                limit: limit,
                withEmbeddings: false,
                cancellationToken: cancellationToken);

            // Memories are sorted by relevance, starting from the most relevant
            await foreach ((MemoryRecord memory, double relevance) in matches.ConfigureAwait(false))
            {
                list.Add((memory, relevance));
            }
        }
        else
        {
            this._log.LogTrace("Fetching relevant memories by filtering");
            IAsyncEnumerable<MemoryRecord> matches = this._memoryDb.GetListAsync(
                index: index,
                filters: filters,
                limit: limit,
                withEmbeddings: false,
                cancellationToken: cancellationToken);

            await foreach (MemoryRecord memory in matches.ConfigureAwait(false))
            {
                list.Add((memory, float.MinValue));
            }
        }

        // Memories are sorted by relevance, starting from the most relevant
        foreach ((MemoryRecord memory, double relevance) in list)
        {
            // Note: a document can be composed by multiple files
            string documentId = memory.GetDocumentId(this._log);

            // Identify the file in case there are multiple files
            string fileId = memory.GetFileId(this._log);

            // Note: this is not a URL and perhaps could be dropped. For now it acts as a unique identifier. See also SourceUrl.
            string linkToFile = $"{index}/{documentId}/{fileId}";

            var partitionText = memory.GetPartitionText(this._log).Trim();
            if (string.IsNullOrEmpty(partitionText))
            {
                this._log.LogError("The document partition is empty, doc: {0}", memory.Id);
                continue;
            }

            // Relevance is `float.MinValue` when search uses only filters and no embeddings (see code above)
            if (relevance > float.MinValue) { this._log.LogTrace("Adding result with relevance {0}", relevance); }

            // If the file is already in the list of citations, only add the partition
            var citation = result.Results.FirstOrDefault(x => x.Link == linkToFile);
            if (citation == null)
            {
                citation = new Citation();
                result.Results.Add(citation);
            }

            // Add the partition to the list of citations
            citation.Index = index;
            citation.DocumentId = documentId;
            citation.FileId = fileId;
            citation.Link = linkToFile;
            citation.SourceContentType = memory.GetFileContentType(this._log);
            citation.SourceName = memory.GetFileName(this._log);
            citation.SourceUrl = memory.GetWebPageUrl(index);

            citation.Partitions.Add(new Citation.Partition
            {
                Text = partitionText,
                Relevance = (float)relevance,
                PartitionNumber = memory.GetPartitionNumber(this._log),
                SectionNumber = memory.GetSectionNumber(),
                LastUpdate = memory.GetLastUpdate(),
                Tags = memory.Tags,
            });

            // In cases where a buggy storage connector is returning too many records
            if (result.Results.Count >= this._config.MaxMatchesCount)
            {
                break;
            }
        }

        if (result.Results.Count == 0)
        {
            this._log.LogDebug("No memories found");
        }

        return result;
    }

    private async Task<IEnumerable<(MemoryRecord Record, double Relevance)>> GetSimilarRecordsAsync(string index, string question, ICollection<MemoryFilter>? filters = null, double minRelevance = 0, CancellationToken cancellationToken = default)
    {
        var chunks = this._memoryDb.GetSimilarListAsync(index, question, filters, minRelevance, limit: this._config.MaxMatchesCount, false, cancellationToken)
                                         .ConfigureAwait(false);

        var result = new List<(MemoryRecord record, double relevance)>();

        await foreach (var chunk in chunks)
        {
            result.Add((chunk.Item1, chunk.Item2));
        }

        return result;
    }

    private async Task<string> Router(string question, IEnumerable<MemoryRecord> records, IContext? context, CancellationToken cancellationToken = default)
    {
        var prompt = GetSKPrompt("StructRAG", "Route")
            .Replace("{{$query}}", question)
            .Replace("{{$titles}}", string.Join(" ", records.DistinctBy(c => c.GetDocumentId()).Select(c => c.GetFileName())));

        var text = new StringBuilder();

        await foreach (var x in this._textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
                            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return text.ToString();
    }


    private async Task<(string instruction, string info)> ConstructAsync(string route,
                                            string question,
                                            IEnumerable<MemoryRecord> records,
                                            IContext? context,
                                            CancellationToken cancellationToken)
    {
        var promptName = string.Empty;

        var chunks = string.Join(Environment.NewLine, records.Select(x => $"{x.GetFileName()}: {x.GetPartitionText()}"));
        var instruction = string.Empty;

        switch (route.ToLowerInvariant())
        {
            case "graph":
                instruction = "Based on the given document, construct a graph where entities are the titles of papers and the relation is 'reference', using the given document title as the head and other paper titles as tails.";
                promptName = "ConstructGraph";
                break;
            case "table":
                instruction = $"Query is {question}, please extract relevant complete tables from the document based on the attributes and keywords mentioned in the Query. Note: retain table titles and source information.";
                promptName = "ConstructTable";
                break;
            case "algorithm":
                instruction = $"Query is {question}, please extract relevant algorithms from the document based on the Query.";
                promptName = "ConstructAlgorithm";
                break;
            case "catalogue":
                instruction = $"Query is {question}, please extract relevant catalogues from the document based on the Query.";
                promptName = "ConstructCatalogue";
                break;
            case "chunk":
                instruction = question;
                return (instruction, chunks);

            default:
                throw new InvalidOperationException();
        }

        var prompt = GetSKPrompt("StructRAG", promptName)
            .Replace("{{$instruction}}", question)
            .Replace("{{$raw_content}}", chunks);

        var text = new StringBuilder();

        await foreach (var x in this._textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
                            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return (instruction, text.ToString());
    }

    private async Task<IEnumerable<string>> DecomposeAsync(string instruction, string info, IContext? context, CancellationToken cancellationToken = default)
    {
        var prompt = GetSKPrompt("StructRAG", "Decompose")
            .Replace("{{$query}}", instruction)
            .Replace("{{$kb_info}}", info);

        var text = new StringBuilder();

        await foreach (var x in this._textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
                            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return text.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<IEnumerable<(string subquery, string subknowledge)>> ExtractAsync(string route, string question, string info, IEnumerable<string> subqueries, IContext? context = null, CancellationToken cancellationToken = default)
    {
        var instruction = string.Empty;

        var subknowledges = new List<(string subquery, string subknowledge)>();

        foreach (var subquery in subqueries)
        {
            switch (route.ToLowerInvariant())
            {
                case "graph":
                    instruction = $"Instruction:\nAnswer the Query based on the given Document.\n\nQuery:\n{subquery}\n\nDocument:\n{info}\n\nOutput:";
                    break;
                case "table":
                    instruction = $"Instruction:\nThe following Tables show multiple independent tables built from multiple documents.\nFilter these tables according to the query, retaining only the table information that helps answer the query.\nNote that you need to analyze the attributes and entities mentioned in the query and filter accordingly.\nThe information needed to answer the query must exist in one or several tables, and you need to check these tables one by one.\n\nTables:{info}\n\nQuery:{subquery}\n\nOutput:";
                    break;
                case "algorithm":
                    instruction = $"Instruction: According to the query, filter out information from algorithm descriptions that can help answer the query.\nNote, carefully analyze the entities and relationships mentioned in the query and filter based on this information.\n\nAlgorithms:{info}\n\nQuery:{subquery}\n\nOutput:";
                    break;
                case "catalogue":
                    instruction = $"Instruction: According to the query, filter out information from the catalogue that can help answer the query.\nNote, carefully analyze the entities and relationships mentioned in the query and filter based on this information.\n\nCatalogues:{info}\n\nQuery:{subquery}\n\nOutput:";
                    break;
                case "chunk":
                    instruction = $"Instruction:\nAnswer the Query based on the given Document.\n\nQuery:\n{subquery}\n\nDocument:\n{info}\n\nOutput:";
                    break;
                default:
                    throw new InvalidOperationException();
            }

            var text = new StringBuilder();

            var results = this._textGenerator
                                    .GenerateTextAsync(instruction, GetTextGenerationOptions(context), cancellationToken)
                                    .ConfigureAwait(false);

            var queryknowledges = new List<string>();

            await foreach (var result in results)
            {
                text.Append(result);
            }

            subknowledges.Add((subquery, text.ToString()));
        }

        return subknowledges;
    }

    private async Task<string> MergeAsync(string chosen, string question, IEnumerable<(string subquery, string subknowledge)> knowledges, IContext? context = null, CancellationToken cancellationToken = default)
    {
        var prompt = GetSKPrompt("StructRAG", "Merge")
            .Replace("{{$query}}", question)
            .Replace("{{$subknowledges}}", string.Join(Environment.NewLine, knowledges.Select(c => $"Subquery: {c.subquery}\nRetrieval results:\n{c.subknowledge}\n\n")));

        var text = new StringBuilder();

        await foreach (var x in this._textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
                            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return text.ToString();
    }

    private TextGenerationOptions GetTextGenerationOptions(IContext? context)
    {
        int maxTokens = context.GetCustomRagMaxTokensOrDefault(this._config.AnswerTokens);
        double temperature = context.GetCustomRagTemperatureOrDefault(this._config.Temperature);
        double nucleusSampling = context.GetCustomRagNucleusSamplingOrDefault(this._config.TopP);

        return new TextGenerationOptions
        {
            MaxTokens = maxTokens,
            Temperature = temperature,
            NucleusSampling = nucleusSampling,
            PresencePenalty = this._config.PresencePenalty,
            FrequencyPenalty = this._config.FrequencyPenalty,
            StopSequences = this._config.StopSequences,
            TokenSelectionBiases = this._config.TokenSelectionBiases,
        };
    }

    private static string GetSKPrompt(string pluginName, string functionName)
    {
        var resourceStream = Assembly.GetExecutingAssembly()
                                     .GetManifestResourceStream($"Prompts/{pluginName}/{functionName}.txt");

        using var reader = new StreamReader(resourceStream!);
        var text = reader.ReadToEnd();
        return text;
    }
}
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
