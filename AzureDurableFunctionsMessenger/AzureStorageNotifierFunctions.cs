using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace AzureDurableFunctionsMessenger
{
    public static class AzureStorageNotifierFunctions
    {
        [FunctionName("AzureStorageNotifier_Orchestrator")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            // Replace "hello" with the name of your Durable Activity Function.
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

        [FunctionName("AzureStorageNotifier_Start")]
        public static async Task HttpBlobStart(
         [BlobTrigger("samples-workitems/{name}", Connection = "StorageConnectionString")] CloudBlockBlob myCloudBlob, string name, ILogger log,
         [DurableClient] IDurableOrchestrationClient starter)
        {
            log.LogInformation($"Started orchestration trigged by BLOB trigger. A blob item with name = '{name}'");
            log.LogInformation($"BLOB Name {myCloudBlob.Name}");
            var blob = myCloudBlob;
            var BlobMetaData = myCloudBlob.Metadata;

            // Function input comes from the request content.
            if (BlobMetaData != null)
            {
                string instanceId = await starter.StartNewAsync("AzureStorageNotifier_Orchestrator", null);

                log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
            }

        }
    }
}