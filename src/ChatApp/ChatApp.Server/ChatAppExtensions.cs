﻿using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Storage.Blobs;
using ChatApp.Server.Models;
using ChatApp.Server.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;

namespace ChatApp.Server;

internal static class ChatAppExtensions
{
    // this should happen before AddChatServices...
    internal static void AddOptions(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AzureAdOptions>(config.GetSection(nameof(AzureAdOptions)));
        services.Configure<AISearchOptions>(config.GetSection(nameof(AISearchOptions)));
        services.Configure<OpenAIOptions>(config.GetSection(nameof(OpenAIOptions)));
        services.Configure<CosmosOptions>(config.GetSection(nameof(CosmosOptions)));
        services.Configure<StorageOptions>(config.GetSection(nameof(StorageOptions)));
        services.Configure<FhirOptions>(config.GetSection(nameof(FhirOptions)));

        // FrontendSettings class needs work on json serialization before this is useful...
        services.Configure<FrontendSettings>(config.GetSection(nameof(FrontendSettings)));
    }

    internal static void AddChatAppServices(this IServiceCollection services, IConfiguration config)
    {
        var azureAdOptions = config.GetSection(nameof(AzureAdOptions)).Get<AzureAdOptions>();
        var frontendSettings = config.GetSection(nameof(FrontendSettings)).Get<FrontendSettings>();

        var defaultAzureCreds = string.IsNullOrEmpty(azureAdOptions.TenantId) ? new DefaultAzureCredential()
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = azureAdOptions.TenantId });

        services.AddSingleton<ChatCompletionService>();

        services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<AISearchOptions>>().Value ?? throw new Exception($"{nameof(AISearchOptions)} is rquired in settings.");

            if (string.IsNullOrWhiteSpace(options?.ApiKey))
            {
                var adOptions = services.GetRequiredService<IOptions<AzureAdOptions>>().Value;                

                return new SearchClient(
                        new Uri(options!.Endpoint),
                        options.IndexName,
                        defaultAzureCreds);
            }

            return new SearchClient(
                    new Uri(options.Endpoint),
                    options.IndexName,
                    new AzureKeyCredential(options.ApiKey));
        });

        services.AddSingleton<AzureSearchService>();

        var isChatEnabled = frontendSettings?.HistoryEnabled ?? false;

        if (isChatEnabled)
        {
            services.AddSingleton(services =>
            {
                var options = services.GetRequiredService<IOptions<CosmosOptions>>().Value ?? throw new Exception($"{nameof(CosmosOptions)} is rquired in settings.");

                return string.IsNullOrEmpty(options?.CosmosKey)
                    ? new CosmosClientBuilder(options!.CosmosEndpoint, defaultAzureCreds)
                        .WithSerializerOptions(new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase })
                        .Build()
                    : new CosmosClientBuilder(options.CosmosEndpoint, new AzureKeyCredential(options.CosmosKey))
                        .WithSerializerOptions(new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase })
                        .Build();
            });

            services.AddSingleton<CosmosConversationService>();
        }

        services.AddSingleton(services =>
        {
            var options = config.GetSection(nameof(StorageOptions)).Get<StorageOptions>() ?? throw new Exception($"{nameof(StorageOptions)} is rquired in settings."); ;

            var storageEndpoint = options?.BlobStorageEndpoint;

            storageEndpoint = storageEndpoint?.Substring(0, storageEndpoint.LastIndexOf('/'));
            var containerUri = new Uri($"{storageEndpoint}/{options?.BlobStorageContainerName}");

            if (string.IsNullOrEmpty(options?.BlobStorageConnectionString))
            {
                var adOptions = services.GetRequiredService<IOptions<AzureAdOptions>>().Value;                

                return new BlobContainerClient(containerUri, defaultAzureCreds);
            }

            return new BlobContainerClient(options?.BlobStorageConnectionString, options?.BlobStorageContainerName);
        });

        services.AddSingleton<NoteService>();
    }
}
