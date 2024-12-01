// See https://aka.ms/new-console-template for more information

using KernelMemory.Evaluation.Evaluators;
using KernelMemory.StructRAG;
using Microsoft.Extensions.Configuration;
using Microsoft.KernelMemory;
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
    .WithSimpleTextDb(new SimpleTextDbConfig()
    {
        StorageType = FileSystemTypes.Volatile
    });

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

var answer = await memory.AskAsync(question);

Console.WriteLine("Standard Kernel Memory Answer");
Console.WriteLine(answer.Result);

Console.WriteLine("====");
Console.WriteLine($"Faithfulness: {(await evaluation.EvaluateAsync(answer)).Score}");

var structRagMemory = memoryBuilder
        .WithStructRagSearchClient()
        .Build();

answer = await memory.AskAsync(question);
Console.WriteLine("StructRAG Memory Answer");
Console.WriteLine(answer.Result);

Console.WriteLine("====");
Console.WriteLine($"Faithfulness: {(await evaluation.EvaluateAsync(answer)).Score}");

Console.WriteLine("Press any key to exit");
Console.ReadKey();