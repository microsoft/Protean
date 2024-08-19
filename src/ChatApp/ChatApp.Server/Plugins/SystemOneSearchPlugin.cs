using ChatApp.Server.Models;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace ChatApp.Server.Plugins;

public class SystemOneSearchPlugin(IKernelMemory _memory, JsonSerializerOptions jsonOptions)
{
    [KernelFunction("Search")]
    [Description("Searches for information from SystemOne knowledge. Should only be used after user is validated in SystemOne.")]
    [return: Description("JSON string of search results")]
    public async Task<string> SearchDocumentsAsync(string query)
    {
        // Get results from KernelMemory search
        var result = await _memory.SearchAsync(query, "default", minRelevance: .70, limit: 10);
        List<SupportingContentRecord> citations = [];
        result.Results.ForEach(c =>
        {
            c.Partitions.OrderBy(p => p.PartitionNumber).ToList()
                .ForEach(p => citations.Add(new SupportingContentRecord(
                    c.SourceName, p.Text, c.SourceUrl ?? string.Empty, c.SourceUrl ?? string.Empty, p.PartitionNumber.ToString(), "")));
        });
        // Format results as stringified JSON of SupportingContentRecord        
        return JsonSerializer.Serialize(new ToolContentResponse(citations, [query]), jsonOptions);
    }
}
