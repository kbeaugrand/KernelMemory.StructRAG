
using KernelMemory.StructRAG;

namespace Microsoft.KernelMemory;

public static class IKernelMemoryBuilderExtension
{
    public static IKernelMemoryBuilder WithStructRagSearchClient(this IKernelMemoryBuilder builder)
    {
        return builder.WithCustomSearchClient<StructRAGSearchCient>();
    }
}

