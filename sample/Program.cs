// See https://aka.ms/new-console-template for more information

using KernelMemory.Evaluation.Evaluators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Configuration;
using Microsoft.KernelMemory.FileSystem.DevTools;
using Microsoft.KernelMemory.MemoryStorage.DevTools;
using Microsoft.SemanticKernel;

var completionConfig = new AzureOpenAIConfig();
var embeddingConfig = new AzureOpenAIConfig();
var evaluationConfig = new AzureOpenAIConfig();

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

configurationBuilder.GetSection("AzureOpenAICompletion")
                .Bind(completionConfig);
configurationBuilder.GetSection("AzureOpenAIEmbedding")
                .Bind(embeddingConfig);
configurationBuilder.GetSection("AzureOpenAIEvaluationChatCompletion")
                .Bind(evaluationConfig);

var memoryBuilder = new KernelMemoryBuilder()
    .WithAzureOpenAITextEmbeddingGeneration(embeddingConfig)
    .WithAzureOpenAITextGeneration(completionConfig)
    .WithSearchClientConfig(new SearchClientConfig()
    {
        AnswerTokens = 4096
    })
    .WithCustomTextPartitioningOptions(new TextPartitioningOptions()
    {
        MaxTokensPerLine = 100,
        MaxTokensPerParagraph = 200,
        OverlappingTokens = 25
    })
    .WithSimpleTextDb(new SimpleTextDbConfig()
    {
        StorageType = FileSystemTypes.Volatile
    });

memoryBuilder.AddSingleton(loggerFactory);

var evaluation = new FaithfulnessEvaluator(Kernel.CreateBuilder()
                                                .AddAzureOpenAIChatCompletion(evaluationConfig.Deployment, evaluationConfig.Endpoint, evaluationConfig.APIKey)
                                                .Build());

var memory = memoryBuilder.Build();

await memory
            .ImportDocumentAsync(new Document()
                                    .AddFile("data/AdvancementsinArtificialIntelligence.txt")
                                    .AddFile("data/EffectsofEconomicDownturnonBusinessOperations.txt")
                                    .AddFile("data/ImpactofPrivacyLawsonTechnologyDevelopment.txt"));

var question = "In the current landscape where privacy laws are becoming increasingly stringent, and the global economy is experiencing a downturn, how can a technology company strategically leverage advancements in artificial intelligence (AI) to maintain competitive advantage and financial stability?";

var answer = await memory.AskAsync(question, minRelevance: 0.9);

memoryLogger.LogInformation(answer.Result);
memoryLogger.LogInformation($"Faithfulness: {(await evaluation.EvaluateAsync(answer)).Score}");

var structRagMemory = memoryBuilder
        .WithStructRagSearchClient()
        .Build();

answer = await structRagMemory.AskAsync(question);

structRagLogger.LogInformation(answer.Result);
structRagLogger.LogInformation($"Faithfulness: {(await evaluation.EvaluateAsync(answer)).Score}");

Console.WriteLine("Press any key to exit");
Console.ReadKey();