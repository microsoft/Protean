using Api.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration(builder =>
    {
        builder.AddJsonFile("appsettings.json");
        builder.AddUserSecrets<Program>(true);
    })
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddSingleton<FunctionSettings>();
        services.AddSingleton<IKernelMemory, MemoryServerless>(services =>
        {
            var config = services.GetRequiredService<IConfiguration>();

            var memoryConfiguration = new KernelMemoryConfig();
            var azureOpenAITextConfig = new AzureOpenAIConfig();
            var azureOpenAIEmbeddingConfig = new AzureOpenAIConfig();
            var searchClientConfig = new SearchClientConfig();
            var azDocIntelConfig = new AzureAIDocIntelConfig();
            var azureAISearchConfig = new AzureAISearchConfig();
            var azureBlobConfig = new AzureBlobsConfig();

            config.BindSection("KernelMemory", memoryConfiguration);
            config.BindSection("KernelMemory:Services:AzureOpenAIText", azureOpenAITextConfig);
            config.BindSection("KernelMemory:Services:AzureOpenAIEmbedding", azureOpenAIEmbeddingConfig);
            //config.BindSection("KernelMemory:Services:AzureAIDocIntel", azDocIntelConfig);
            config.BindSection("KernelMemory:Services:AzureAISearch", azureAISearchConfig);
            //config.BindSection("KernelMemory:Services:AzureBlobs", azureBlobConfig);
            config.BindSection("KernelMemory:Retrieval:SearchClient", searchClientConfig);

            var kmBuilder = new KernelMemoryBuilder()
                .Configure(builder => builder.Services.AddLogging(l =>
                {
                    l.SetMinimumLevel(LogLevel.Trace);
                    l.AddSimpleConsole(c => c.SingleLine = true);
                }))
                .AddSingleton(memoryConfiguration)
                .WithAzureAISearchMemoryDb(azureAISearchConfig)              // Store memories in Azure AI Search
                                                                             //.WithAzureBlobsDocumentStorage(azureBlobConfig)              // Store files in Azure Blobs
                .WithAzureOpenAITextGeneration(azureOpenAITextConfig)
                .WithAzureOpenAITextEmbeddingGeneration(azureOpenAIEmbeddingConfig);

            return kmBuilder.Build<MemoryServerless>();
        });        
    })
    .Build();

await host.RunAsync();
