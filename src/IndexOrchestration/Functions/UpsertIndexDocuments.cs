#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
using Api.Models;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;

namespace Api.Functions;

public class UpsertIndexDocuments
{
    private readonly FunctionSettings _functionSettings;
    private readonly ILogger<UpsertIndexDocuments> _logger;
    private readonly IKernelMemory _kernelMemory;

    // todo: move to orchestration function pattern to handle larger throughput
    // todo: add in upsert for individual notes
    public UpsertIndexDocuments(
        FunctionSettings functionSettings, 
        ILogger<UpsertIndexDocuments> logger, 
        IKernelMemory kernelMemory)
    {        
        _functionSettings = functionSettings;
        _logger = logger;
        _kernelMemory = kernelMemory;        
    }

    // todo: is there a way to more dynamically get the path or need it be 'hardcoded'?
    // , Source = BlobTriggerSource.LogsAndContainerScan
    [Function(nameof(UpsertIndexDocuments))]
    public async Task RunAsync([BlobTrigger("docs/{blobName}", Connection = "IncomingBlobConnStr")] Stream blobContents, string blobName)
    {
        _logger.LogInformation("Processing blob {blobName}...", blobName);

        // use regex to create a docId from the blobName. only include the filename without file extension
        // return all but last item of array

        var docId = string.Join('.', blobName.Split('/')[^1].Split('.')[..^1]);

        // test this to see if it handles upsert
        await _kernelMemory.ImportDocumentAsync(blobContents, blobName, docId);
    }

    private async Task DeleteOldChunksAsync(string nodeId)
    {
        await Task.Delay(0);
        throw new NotImplementedException();
        //var lastChunkIndex = documents
        //    .OrderBy(c => c[IndexFields.NoteChunkOrder])
        //    .Select(c => (int)c[IndexFields.NoteChunkOrder])
        //    .LastOrDefault();

        //var oldChunksToDelete = new List<string>();

        //// intellisense not recognizing NoteId cannot be null here...
        //await foreach (var indexRecordId in GetExistingIndexRecordsAsync(noteId))
        //{
        //    var oldChunkOrder = int.Parse(indexRecordId.Split('-')[^1]);

        //    if (oldChunkOrder > lastChunkIndex)
        //        oldChunksToDelete.Add(indexRecordId);
        //}

        //Response<IndexDocumentsResult>? deleteDocumentsResult = null;

        //if (oldChunksToDelete.Count > 0)
        //{
        //    _logger.LogInformation("Deleting {count} old chunks for note {noteId}...", oldChunksToDelete.Count, noteId);

        //    _logger.LogTrace("Old chunks to delete: {chunks}", string.Join(',', oldChunksToDelete));

        //    try
        //    {
        //        deleteDocumentsResult = await _searchClient.DeleteDocumentsAsync(oldChunksToDelete.Select(s => new NoteRecordPointer(s)));
        //    }
        //    catch (AggregateException aggregateException)
        //    {
        //        _logger.LogError("Partial failures detected. Some documents failed to delete.");

        //        foreach (var exception in aggregateException.InnerExceptions)
        //        {
        //            _logger.LogError("{exception}", exception.Message);
        //        }
        //    }

        //    foreach (var deleteResult in deleteDocumentsResult?.Value?.Results ?? [])
        //    {
        //        _logger.LogTrace("Deleted document {id} with status {status}.", deleteResult.Key, deleteResult.Status);
        //    }
        //}
    }    

}


#pragma warning restore SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
