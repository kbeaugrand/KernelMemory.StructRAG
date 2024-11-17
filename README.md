# Struct RAG search client for KernelMemory

[![Build & Test](https://github.com/kbeaugrand/KernelMemory.StructRAG/actions/workflows/build_tests.yml/badge.svg)](https://github.com/kbeaugrand/KernelMemory.StructRAG/actions/workflows/build_test.yml)
[![Create Release](https://github.com/kbeaugrand/KernelMemory.StructRAG/actions/workflows/publish.yml/badge.svg)](https://github.com/kbeaugrand/KernelMemory.StructRAG/actions/workflows/publish.yml)
[![Version](https://img.shields.io/github/v/release/kbeaugrand/KernelMemory.StructRAG)](https://img.shields.io/github/v/release/kbeaugrand/KernelMemory.StructRAG)
[![License](https://img.shields.io/github/license/kbeaugrand/KernelMemory.StructRAG)](https://img.shields.io/github/v/release/kbeaugrand/KernelMemory.StructRAG)

> Note: Freely inspired from [StructRag](https://arxiv.org/abs/2410.08815), this is an implemention of a custom seach client for [Kernel Memory](https://github.com/microsoft/kernel-memory).

## Overview
Welcome to the SearchClient for KernelMemory repository! This project leverages the innovative StructRAG methodology to enhance the accuracy of Retrieval-Augmented Generation (RAG) in complex scenarios. By integrating StructRAG with KernelMemory, we aim to provide a robust solution for knowledge-intensive reasoning tasks.

## What is StructRAG?
StructRAG is a novel framework designed to improve the performance of RAG by converting raw information into structured knowledge. This approach is inspired by cognitive theories, which suggest that humans process information more effectively when it is organized into meaningful structures. StructRAG identifies the optimal structure type for a given task, reconstructs original documents into this format, and infers answers based on the resulting structure. This method excels in scenarios where information is scattered and requires global reasoning.

![https://arxiv.org/html/2410.08815v2/x1.png](https://arxiv.org/html/2410.08815v2/x1.png)

More info at: [https://arxiv.org/abs/2410.08815](https://arxiv.org/abs/2410.08815)

## What is KernelMemory?
KernelMemory (KM) is a multi-modal AI service that specializes in the efficient indexing of datasets through custom continuous data hybrid pipelines. It supports various advanced features, including:

* Retrieval-Augmented Generation (RAG)
* Synthetic memory
* Prompt engineering
* Custom semantic memory processing

KM is available as a Web Service, Docker container, Plugin for ChatGPT/Copilot/Semantic Kernel, and as a .NET library for embedded applications. It enables natural language querying to obtain answers from indexed data, complete with citations and links to original sources.

More info at: [https://github.com/microsoft/kernel-memory](https://github.com/microsoft/kernel-memory)

## Installation

To install the assistant Framework, you need to add the required nuget package to your project:

```dotnetcli
dotnet add package KernelMemory.StructRAG
```

### Configuration

Configure you KernelMemory client to use the custom search client: 
```
using KernelMemory.StructRAG;

var memory = new KernelMemoryBuilder()
    .WithOpenAIDefaults(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
    .WithCustomSearchClient<StructRAGSearchCient>()
    .Build<MemoryServerless>();
```

Then use as usual... =)

## Usage

1. Create you agent description file in yaml: 
    
    ```

## License

This project is licensed under the [MIT License](LICENSE).
