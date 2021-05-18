using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace DurableAzureStorageFunctions
{
    public static class ServerlessAzureStorageNotifier
    {
        [FunctionName("AzureStorageNotifier_Orchestrator")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            var eventData = context.GetInput<CloudBlobItem>();


            outputs.Add(await context.CallActivityAsync<string>("Function1_Hello", "Tokyo"));
            outputs.Add(await context.CallActivityAsync<string>("Function1_Hello", "Seattle"));
            outputs.Add(await context.CallActivityAsync<string>("Function1_Hello", "London"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return outputs;
        }

        [FunctionName("Function1_Hello")]
        public static string SayHello([ActivityTrigger] string name, ILogger log)
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }

        //[FunctionName("Function1_HttpStart")]
        //public static async Task<HttpResponseMessage> HttpStart(
        //    [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
        //    [DurableClient] IDurableOrchestrationClient starter,
        //    ILogger log)
        //{
        //    // Function input comes from the request content.
        //    string instanceId = await starter.StartNewAsync("Function1", null);

        //    log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

        //    return starter.CreateCheckStatusResponse(req, instanceId);
        //}

        [FunctionName("AzureStorageNotifier_Start")]
        public static async Task HttpBlobStart(
        [BlobTrigger("samples-workitems/{name}", Connection = "StorageConnectionString")] CloudBlockBlob myCloudBlob, string name, ILogger log,
        [DurableClient] IDurableOrchestrationClient starter)
        {
            log.LogInformation($"Started orchestration trigged by BLOB trigger. A blob item with name = '{name}'");
            log.LogInformation($"BLOB Name {myCloudBlob.Name}");
          
            // Function input comes from the request content.
            if (myCloudBlob != null)
            {
                var newUploadedBlobItem = new CloudBlobItem {
                    Name = myCloudBlob.Name,
                    BlobUrl = myCloudBlob.Uri.AbsoluteUri.ToString(),
                    Metadata = (Dictionary<string, string>)myCloudBlob.Metadata,
                    FileType = myCloudBlob.BlobType.ToString(),
                    Size = myCloudBlob.Name.Length.ToString(),
                    ETag = myCloudBlob.Properties.ETag.ToString()
                };
                
               // myCloudBlob.SerializeObjectToBlobAsync(newUploadedBlobItem).Wait();

                var instanceId = await starter.StartNewAsync("AzureStorageNotifier_Orchestrator", newUploadedBlobItem);

                log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
            }

        }

    }
}