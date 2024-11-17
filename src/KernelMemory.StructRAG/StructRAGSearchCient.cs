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
        var records = await GetSimilarRecordsAsync(index, question, filters, minRelevance, cancellationToken)
                                      .ConfigureAwait(false);

        if (!records.Any())
        {
            return new MemoryAnswer
            {
                Question = question,
                Result = this._config.EmptyAnswer
            };
        }

        // 1. router
        var route = await Router(question, records, context, cancellationToken)
                            .ConfigureAwait(false);

        // 2. structurizer
        (var instruction, var info) = await ConstructAsync(route, question, records, context, cancellationToken)
                                            .ConfigureAwait(false);

        // 3. utilizer
        var subqueries = await DecomposeAsync(instruction, info, context, cancellationToken)
                                        .ConfigureAwait(false);

        var subknowledges = await ExtractAsync(route, question, info, subqueries, context, cancellationToken)
                                        .ConfigureAwait(false);

        var answer = await MergeAsync(route, question, subknowledges, context, cancellationToken)
                                        .ConfigureAwait(false);

        return new MemoryAnswer()
        {
            Question = question,
            Result = answer,
            NoResult = false,
            RelevantSources = records
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

    public Task<SearchResult> SearchAsync(string index, string query, ICollection<MemoryFilter>? filters = null, double minRelevance = 0, int limit = -1, IContext? context = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private async Task<IEnumerable<MemoryRecord>> GetSimilarRecordsAsync(string index, string question, ICollection<MemoryFilter>? filters = null, double minRelevance = 0, CancellationToken cancellationToken = default)
    {
        var chunks = this._memoryDb.GetSimilarListAsync(index, question, filters, minRelevance, limit: 50, false, cancellationToken)
                                         .ConfigureAwait(false);

        var result = new List<MemoryRecord>();

        await foreach (var chunk in chunks)
        {
            result.Add(chunk.Item1);
        }

        return result;
    }

    private async Task<string> Router(string question, IEnumerable<MemoryRecord> records, IContext? context, CancellationToken cancellationToken = default)
    {
        var prompt = GetSKPrompt("StructRag", "Route")
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
                instruction = "construct chunk";
                return (instruction, chunks);

            default:
                throw new InvalidOperationException();
        }

        var prompt = GetSKPrompt("StructRag", promptName)
            .Replace("{{$instruction}}", question)
            .Replace("{{$raw_content}}", chunks);

        var text = new StringBuilder();

        await foreach (var x in this._textGenerator.GenerateTextAsync(prompt, GetTextGenerationOptions(context), cancellationToken)
                            .ConfigureAwait(false))
        {
            text.Append(x);
        }

        return  (instruction, text.ToString());
    }

    private async Task<IEnumerable<string>> DecomposeAsync(string instruction, string info, IContext? context, CancellationToken cancellationToken = default)
    {
        var prompt = GetSKPrompt("StructRag", "Decompose")
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
                    instruction = "Instruction:\nAnswer the Query based on the given Document.\n\nQuery:\n{composed_query}\n\nDocument:\n{chunk}\n\nOutput:";
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
        var prompt = GetSKPrompt("StructRag", "Merge")
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
