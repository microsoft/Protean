using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace ChatApp.Server.Plugins;

public class ReplaceCardPlugin(IHttpClientFactory httpClientFactory)
{    
    [KernelFunction("ReplaceCard")]
    [Description("Initiates a request to replace a card in SystemOne with the provided card number. Should only be used after user is validated in SystemOne.")]
    public async Task<string> ReplaceCard(string cardNumber)
    {
        await Task.Delay(0); // Do this until we have actual async code
        using var client = httpClientFactory.CreateClient();
        // call SystemOne API to replace card HERE

        return $"Request to replace card with ID: {cardNumber} was created.";
    }
}
