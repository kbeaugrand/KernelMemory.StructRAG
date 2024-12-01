## Struct RAG Sample

This sample use a synthetic data that is stored in [data] folder containing 3 files that are imported into a volatile memory.
Then we use Kernel Memory with gpt-4o-mini to generate an answer regarding the same complex question and evaluate the answer using a faithfulness evaluator running with gpt-4o-mini model.

**Initialization**

```cs

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
```

**Ask the question**

```cs

var answer = await memory.AskAsync(question);

Console.WriteLine("Standard Kernel Memory Answer");
Console.WriteLine(answer.Result);
```

> Output: 
 ```
In the current landscape characterized by stringent privacy laws and an economic downturn, a technology company can strategically leverage advancements in artificial intelligence (AI) to maintain competitive advantage and financial stability through several key approaches:

1. **Enhancing Compliance with Privacy Regulations**: AI can be utilized to develop advanced data management systems that ensure compliance with evolving privacy laws. By automating data classification, access controls, and monitoring, companies can reduce the risk of non-compliance and associated penalties. This not only protects the company legally but also builds trust with customers who are increasingly concerned about data privacy.
2. **Improving Operational Efficiency**: AI technologies can streamline internal processes, reducing operational costs and improving efficiency. For instance, AI-driven analytics can optimize supply chain management, customer service, and resource allocation. By implementing AI solutions that enhance productivity, companies can mitigate the financial impacts of an economic downturn and maintain profitability.
3. **Driving Innovation in Product Offerings**: The integration of AI into products can lead to the development of smarter, more personalized solutions that meet customer needs more effectively. This innovation can differentiate a company in a competitive market, attracting new customers and retaining existing ones. As seen in the financial highlights, companies that invest in AI have reported significant revenue increases, indicating strong market demand for AI-enhanced products.
4. **Creating New Revenue Streams**: AI can open up new business models and revenue opportunities. For example, companies can offer AI-as-a-Service (AIaaS) or develop subscription-based models for AI-driven applications. This diversification can help stabilize revenue during economic fluctuations.
5. **Leveraging Customer Insights**: AI can analyze customer data to provide insights into preferences and behaviors, allowing companies to tailor their marketing strategies and product offerings. By understanding customer needs better, companies can enhance customer satisfaction and loyalty, which is crucial during challenging economic times.
6. **Cost Management and Resource Allocation**: AI can assist in identifying areas for cost reduction and optimizing resource allocation. By analyzing operational data, AI can highlight inefficiencies and suggest improvements, enabling companies to implement cost-saving measures without sacrificing quality.
7. **Fostering a Culture of Innovation**: By prioritizing AI research and development, companies can cultivate a culture of innovation that encourages employees to explore new ideas and solutions. This proactive approach can lead to breakthroughs that not only address current challenges but also position the company for future growth.

In summary, by strategically leveraging advancements in AI, technology companies can enhance compliance with privacy laws, improve operational efficiency, drive product innovation, create new revenue streams, gain customer insights, manage costs effectively, and foster a culture of innovation. These strategies can help maintain competitive advantage and financial stability in a challenging economic environment.
```

**Evaluation**

We use Kernel Memory Evaluator to estimate the faithfulness of the answer regarding the context and the data.
> Note, the faithfulness is estimated by gpt-4o-mini

```cs
Console.WriteLine($"Faithfulness: {await evaluation.Evaluate(answer, new Dictionary<string, object?>())}");
```

> Faithfullness: 0.55263156

### With StructRAG

We can now do the same with StructRAG search client: 

```cs
var structRagMemory = memoryBuilder
        .WithStructRagSearchClient()
        .Build();

answer = await memory.AskAsync(question);
Console.WriteLine("StructRAG Memory Answer");
Console.WriteLine(answer.Result);
```

> Output: 
 ```
In the current landscape characterized by stringent privacy laws and an economic downturn, a technology company can strategically leverage advancements in artificial intelligence (AI) to maintain competitive advantage and financial stability through several key approaches:

1. **Privacy-Centric AI Solutions**: As privacy laws evolve, companies can develop AI technologies that prioritize user privacy. This includes implementing advanced encryption methods and data anonymization techniques that comply with regulations while still providing valuable insights. By positioning themselves as leaders in privacy-centric AI, companies can enhance user trust and satisfaction, which is crucial for customer retention in a competitive market.
2. **Operational Efficiency**: AI can streamline internal processes, reduce operational costs, and improve efficiency. By automating routine tasks and optimizing workflows, companies can mitigate the financial impact of the economic downturn. For instance, AI-driven analytics can help identify areas for cost savings and resource allocation, allowing businesses to operate more effectively even in challenging economic conditions.
3. **Enhanced Product Offerings**: Companies can leverage AI to innovate and enhance their product offerings. By integrating AI capabilities into existing products, businesses can provide smarter, more efficient solutions that meet evolving customer needs. This not only helps in retaining current customers but also attracts new ones, driving revenue growth despite economic challenges.
4. **New Revenue Streams**: The development of AI technologies can open up new revenue streams. For example, companies can create AI-as-a-Service platforms or offer AI-driven insights and analytics to other businesses. This diversification can help stabilize revenue during economic downturns and reduce reliance on traditional income sources.
5. **Customer Insights and Personalization**: AI can analyze customer data to provide insights into preferences and behaviors, enabling companies to offer personalized experiences. This level of customization can enhance customer loyalty and satisfaction, which is particularly important in a competitive landscape where consumers are increasingly selective about the brands they engage with.
6. **Proactive Risk Management**: AI can be utilized to predict and manage risks associated with economic fluctuations. By analyzing market trends and consumer behavior, companies can make informed decisions that mitigate potential losses and capitalize on emerging opportunities.
7. **Investment in R&D**: Continuing to invest in AI research and development is crucial. By dedicating resources to innovate and improve AI technologies, companies can stay ahead of competitors and adapt to changing market conditions. This investment can also lead to breakthroughs that align with privacy regulations, further enhancing the company's reputation and market position.
In summary, by focusing on privacy-centric AI solutions, enhancing operational efficiency, innovating product offerings, exploring new revenue streams, personalizing customer experiences, managing risks proactively, and investing in R&D, technology companies can strategically navigate the challenges posed by stringent privacy laws and economic downturns, thereby maintaining competitive advantage and financial stability.```
```

> Faithfullness: 0.7826087