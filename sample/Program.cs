// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI.Ollama;
using Microsoft.KernelMemory.Configuration;

var ollamaConfig = new OllamaConfig();
var qdrantConfig = new QdrantConfig();

var index = "demo-struct-rag";

IConfiguration configurationBuilder = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .ClearProviders()
        .AddConsole()
        .AddConfiguration(configurationBuilder.GetSection("Logging"));
});

var memoryLogger = loggerFactory.CreateLogger("Standard Kernel Memory");
var structRagLogger = loggerFactory.CreateLogger("StructRAG Kernel Memory");

configurationBuilder.GetSection("Ollama")
                .Bind(ollamaConfig);
configurationBuilder.GetSection("Qdrant")
                .Bind(qdrantConfig);

var memoryBuilder = new KernelMemoryBuilder()
    .WithOllamaTextGeneration(ollamaConfig)
    .WithOllamaTextEmbeddingGeneration(ollamaConfig)
    .WithSearchClientConfig(new SearchClientConfig()
    {
        AnswerTokens = 4096        
    })
    .WithCustomTextPartitioningOptions(new TextPartitioningOptions()
    {
        MaxTokensPerLine = 100,
        MaxTokensPerParagraph = 500,
        OverlappingTokens = 50
    })
    .WithQdrantMemoryDb(qdrantConfig);

memoryBuilder.AddSingleton(loggerFactory);

var memory = memoryBuilder.Build();

var indexes = await memory
                        .ListIndexesAsync();

if (indexes.Any(i => String.Equals(i.Name, index, StringComparison.OrdinalIgnoreCase)))
{
    await memory.DeleteIndexAsync(index);
}

var documentID = await memory
                            .ImportDocumentAsync(new Document()
                                                    .AddFile("data/01.Overview.txt")
                                                    .AddFile("data/02.Revenue Breakdown (in USD Millions).txt")
                                                    .AddFile("data/04.Research & Development (R&D) Investment.txt")
                                                    .AddFile("data/06.Employee Metrics.txt"), index: index);

var memoryFilter = MemoryFilters.ByDocument(documentID);

var question = "In one simple sentence, comparing investments and revenues what is the most efficient company in artificial intelligence services among all the competitors?";

memoryLogger.LogInformation("==========================================================================");

var answer = await memory.AskAsync(question,
                                    index: index,
                                    filter: memoryFilter, 
                                    minRelevance: .6f);

memoryLogger.LogInformation(answer.Result);

memoryLogger.LogInformation("Press any key to continue");
Console.ReadKey();

structRagLogger.LogInformation("==========================================================================");

var structRagMemory = memoryBuilder
        .WithStructRagSearchClient()
        .Build();

answer = await structRagMemory.AskAsync(question,
                                        index: index,
                                        filter: memoryFilter,
                                        minRelevance: .6f);

structRagLogger.LogInformation(answer.Result);

structRagLogger.LogInformation("Press any key to exit");
Console.ReadKey();