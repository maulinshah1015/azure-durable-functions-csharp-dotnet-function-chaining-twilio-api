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

            var uploadedCloudBlob = context.GetInput<CloudBlobItem>();


            outputs.Add(await context.CallActivityAsync<string>("Function1_Hello", "Tokyo"));
            outputs.Add(await context.CallActivityAsync<string>("Function1_Hello", "Seattle"));
            outputs.Add(await context.CallActivityAsync<string>("Function1_Hello", "London"));

            // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
            return outputs;
        }

        [FunctionName("AzureStorageNotifier_SendMessageToAzureServiceBusQueue")]
        public static string SendMessageToAzureServiceBusQueue([ActivityTrigger] CloudBlobItem uploadedcloudBlob, ILogger log)
        {
            log.LogInformation($"Received event data with an uploaded cloud blob {uploadedcloudBlob.Name} with format {uploadedcloudBlob.FileType}.");

            //TODO
            return $"Composed Message for uploaded cloud blob {uploadedcloudBlob.Name}!";
        }


        [FunctionName("AzureStorageNotifier_SendMessageToUserViaTwilioAPI")]
        public static string Se([ActivityTrigger] CloudBlobItem uploadedcloudBlob, ILogger log)
        {
            log.LogInformation($"Received event data with an uploaded cloud blob {uploadedcloudBlob.Name} with format {uploadedcloudBlob.FileType}.");

            //TODO
            return $"Composed Message for uploaded cloud blob {uploadedcloudBlob.Name}!";
        }


        //[FunctionName("QueueTwilio")]
        //[return: TwilioSms(AccountSidSetting = "TwilioAccountSid", AuthTokenSetting = "TwilioAuthToken", From = "+1425XXXXXXX")]
        //public static CreateMessageOptions Run(
        //[QueueTrigger("myqueue-items", Connection = "AzureWebJobsStorage")] JObject order,
        //ILogger log)
        //{
        //    log.LogInformation($"C# Queue trigger function processed: {order}");

        //    var message = new CreateMessageOptions(new PhoneNumber(order["mobileNumber"].ToString()))
        //    {
        //        Body = $"Hello {order["name"]}, thanks for your order!"
        //    };

        //    return message;
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