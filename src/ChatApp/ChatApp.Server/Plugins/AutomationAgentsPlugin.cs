#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using Azure.Identity;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;
using System.Text.Json;

namespace ChatApp.Server.Plugins;

public class AutomationAgentsPlugin(AzureOpenAIConfig options, IConfiguration config)
{
    [KernelFunction("SystemOneAutomationAgent")]
    [Description("Initiates a SystemOne agent with access to instruction sets and API actions to automate tasks requested and approved by user chat. This agent should only be used after user is validated in SystemOne")]
    [return: Description("The completion status of the process")]
    public async Task<string> SystemOneAutomationAgent(string prompt)
    {
        var textConfig = options;
        var promptSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 1024,
            Temperature = 0.5,
            StopSequences = [],
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };
        var kernel = GetKernel(textConfig, config["AZURE_TENANT_ID"] ?? "");

        // add the plugins we want for this agent here
        kernel.Plugins.AddFromType<ReplaceCardPlugin>();
        kernel.Plugins.AddFromType<SystemOneSearchPlugin>();

        ChatCompletionAgent memberServiceAgent = new ChatCompletionAgent()
        {   
            Name = "SystemOneAutomationAgent",
            Instructions = $$$"""
            You are a customer service agent with access to SystemOne actions and applications required for performing tasks on behalf of a requesting user. 
            Your job is to the complete tasks requested by the user based on instructions and actions available to you.
            When the tasks are complete, say "complete".
            """,
            Kernel = kernel,
            ExecutionSettings = promptSettings
        };

        ChatHistory history = new();
        history.AddMessage(AuthorRole.User, prompt);

        return JsonSerializer.Serialize(await memberServiceAgent.InvokeAsync(history).ToListAsync());
    }

    [KernelFunction("SystemTwoAutomationAgent")]
    [Description("Initiates a SystemTwo agent with access to instruction sets and API actions to automate tasks requested and approved by user chat. This agent should only be used after user is validated in SystemTwo")]
    [return: Description("The completion status of the process")]
    public async Task<string> SystemTwoAutomationAgent(string prompt)
    {
        var textConfig = options;
        var promptSettings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = 1024,
            Temperature = 0.5,
            StopSequences = [],
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };
        var kernel = GetKernel(textConfig, config["AZURE_TENANT_ID"] ?? "");

        // add the plugins we want for this agent here
        //kernel.Plugins.AddFromType<ReplaceCardPlugin>();
        //kernel.Plugins.AddFromType<SystemOneSearchPlugin>();

        ChatCompletionAgent memberServiceAgent = new ChatCompletionAgent()
        {
            Name = "SystemTwoAutomationAgent",
            Instructions = $$$"""
            You are a customer service agent with access to SystemTwo actions and applications required for performing tasks on behalf of a requesting user. 
            Your job is to the complete tasks requested by the user based on instructions and actions available to you.
            When the tasks are complete, say "complete".
            """,
            Kernel = kernel,
            ExecutionSettings = promptSettings
        };

        ChatHistory history = new();
        history.AddMessage(AuthorRole.User, prompt);

        return JsonSerializer.Serialize(await memberServiceAgent.InvokeAsync(history).ToListAsync());
    }

    private static Kernel GetKernel(AzureOpenAIConfig textConfig, string azureTenantId = "")
    {
        var builder = Kernel.CreateBuilder();

        builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Trace));

        if (string.IsNullOrEmpty(textConfig.APIKey)) // use managed identity
        {
            var defaultAzureCreds = string.IsNullOrWhiteSpace(azureTenantId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(
                    new DefaultAzureCredentialOptions
                    {
                        TenantId = azureTenantId
                    });            

            builder = builder.AddAzureOpenAIChatCompletion(
                textConfig.Deployment,
                textConfig.Endpoint,
                defaultAzureCreds);
        }
        else // use api key
        {
            builder = builder.AddAzureOpenAIChatCompletion(
                textConfig.Deployment,
                textConfig.Endpoint,
                textConfig.APIKey);
        }

        return builder.Build();
    }
}

#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
