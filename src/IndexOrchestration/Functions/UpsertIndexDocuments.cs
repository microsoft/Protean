#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;

namespace Api.Functions;

public class UpsertIndexDocuments
{
    private readonly ILogger<UpsertIndexDocuments> _logger;
    private readonly IKernelMemory _kernelMemory;

    // todo: move to orchestration function pattern to handle larger throughput
    // todo: add in upsert for individual notes
    public UpsertIndexDocuments(
        ILogger<UpsertIndexDocuments> logger, 
        IKernelMemory kernelMemory)
    {        
        _logger = logger;
        _kernelMemory = kernelMemory;
    }

    /// <summary>
    /// Blob triggered function to index documents into Kernel Memory service.
    /// filepath: docs/{index}/{docId}/{blobName}
    /// </summary>
    /// <param name="blobContents"></param>
    /// <param name="index">The target AI search index name.</param>
    /// <param name="docId">The KernelMemory document ID.</param>
    /// <param name="blobName">The name of the file.</param>
    /// <returns></returns>
    [Function(nameof(UpsertIndexDocuments))]
    public async Task RunAsync([BlobTrigger("docs/{index}/{docId}/{blobName}", Connection = "IncomingBlobConnStr")] Stream blobContents, 
        string index,
        string docId,
        string blobName)
    {
        _logger.LogInformation("Processing blob {blobName}...", blobName);
        if (string.IsNullOrWhiteSpace(blobName))
        {
            _logger.LogWarning("Blob name is empty. Skipping...");
            return;
        }
        if (string.IsNullOrWhiteSpace(docId))
        {
            _logger.LogWarning("Document ID is empty. Skipping...");
            return;
        }
        if (string.IsNullOrWhiteSpace(index))
        {
            _logger.LogWarning("Index name is empty. Skipping...");
            return;
        }
        
        await _kernelMemory.ImportDocumentAsync(blobContents, blobName, docId, index: index);
    }      

}

#pragma warning restore SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
