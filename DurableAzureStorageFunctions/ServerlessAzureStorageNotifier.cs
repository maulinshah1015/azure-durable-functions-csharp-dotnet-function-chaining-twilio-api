using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Twilio;


namespace DurableAzureStorageFunctions
{
    public static class ServerlessAzureStorageNotifier
    {
        static List<string> queueMessages = new List<string>();

        [FunctionName("AzureStorageNotifier_Orchestrator")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var outputs = new List<string>();

            var uploadedCloudBlob = context.GetInput<CloudBlobItem>();

            var isMessageSentToServiceBusQueue = await context.CallActivityAsync<bool>("AzureStorageNotifier_SendMessageToAzureServiceBusQueue", uploadedCloudBlob);

            if (isMessageSentToServiceBusQueue == true && queueMessages.Count > 0)
            {
                //Send message using Twilio
            }
            return outputs;
        }

       
        [FunctionName("AzureStorageNotifier_SendMessageToAzureServiceBusQueue")]
        [return: ServiceBus("azdurablefunctioncloudqueue", EntityType.Queue, Connection = "AzureServiceBusConnectionString")]
        public static async Task<bool> SendMessageToAzureServiceBusQueueAsync([ActivityTrigger] CloudBlobItem uploadedcloudBlob, ILogger log, ExecutionContext executionContext)
        {
            try
            {
                log.LogInformation($"Received event data with an uploaded cloud blob {uploadedcloudBlob.Name} with format {uploadedcloudBlob.FileType}.");

                //Config settings for Azure Service Bus
                var azureServiceBusConfig = new ConfigurationBuilder()
                     .SetBasePath(executionContext.FunctionAppDirectory)
                     .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                     .AddEnvironmentVariables()
                     .Build();

                var serviceBusConnection = azureServiceBusConfig["AzureServiceBusConnectionString"];
                var serviceBusQueue = azureServiceBusConfig["ServiceBusQueueName"];

             
                 log.LogInformation($"Composing message to be sent to the queue");

                 var composedMessage = $"A blob image {uploadedcloudBlob.Name} was uploaded to Azure Service Bus Queue azdurablefunctioncloudqueue. </br> " +
                                      $"Blob Type: {uploadedcloudBlob.FileType} </br> " +
                                      $"Blob URL: {uploadedcloudBlob.BlobUrl} </br> " +
                                      $"Message sent via Azure Durable Functions App made by Jonah";

                await using (ServiceBusClient client = new ServiceBusClient(serviceBusConnection))
                {
                    //Create sender 
                    ServiceBusSender sender = client.CreateSender(serviceBusQueue);

                    //Create message 
                    ServiceBusMessage message = new ServiceBusMessage(composedMessage);

                    //Send Message to ServiceBus Queue
                    await sender.SendMessageAsync(message);
                    log.LogInformation($"Sent a message to Service Bus Queue: {serviceBusQueue}");
                    return true;
                }

            }
            catch (Exception ex)
            {
                log.LogInformation($"Something went wrong sending the message to the queue : {ServiceBusQueueName}. Exception {ex.InnerException}" );
                throw;
            }
        }


        [FunctionName("AzureStorageNotifier_SendMessageToUserViaTwilioAPI")]
        public static async Task<bool> SendMessageTwiliosync([ActivityTrigger] CloudBlobItem uploadedcloudBlob, ILogger log, ExecutionContext executionContext)
        {
            log.LogInformation($"Received event data with an uploaded cloud blob {uploadedcloudBlob.Name} with format {uploadedcloudBlob.FileType}.");

            //TODO
            return true;
        }


        [FunctionName("AzureStorageNotifier_AzureServiceBusTriggeredByUploadedBlob")]
        public static async Task<bool> AzureServiceBusTriggeredByUploadedBlob(
            [ServiceBusTrigger("azdurablefunctioncloudqueue", Connection = "AzureServiceBusConnectionString")] 
            string queueMessageItem, Int32 deliveryCount, DateTime enqueuedTimeUtc, string messageid,
            ILogger log, ExecutionContext executionContext)
        {
            log.LogInformation($"Azure Service Bus Queue = azdurablefunctioncloudqueue was trigged by uploaded data");

            //Config for Azure Service Bus 
            var azureServiceBusConfig = new ConfigurationBuilder()
                 .SetBasePath(executionContext.FunctionAppDirectory)
                 .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                 .AddEnvironmentVariables()
                 .Build();

            var serviceBusConnection = azureServiceBusConfig["AzureServiceBusConnectionString"];
            var serviceBusQueue = azureServiceBusConfig["ServiceBusQueueName"];


            //Received queue message date on triggered event uploaded blob
            await using (ServiceBusClient client = new ServiceBusClient(serviceBusConnection))
            {
                // Create a message processor to process queue message
                ServiceBusProcessor serviceBusProcessor = client.CreateProcessor(serviceBusQueue, new ServiceBusProcessorOptions());

                //Queue message handler
                serviceBusProcessor.ProcessMessageAsync += ServiceBusQueueMessageHandler;

                //process possible errors
                serviceBusProcessor.ProcessErrorAsync += ServiceBusQueueMessageErrorHandler;

                //Start processing
                await serviceBusProcessor.StartProcessingAsync();

                //For processing
                Console.WriteLine($"Processing wait for a minute. Process any key to end processing");
                Console.ReadKey();

                //TODO 
                //Stop manually processing 
                await serviceBusProcessor.StopProcessingAsync();
                Console.WriteLine($"Stopped receiving messages");

                return true;
            }

            //TODO
            return true;
        }

        private static Func<ProcessMessageEventArgs, Task> ServiceBusQueueMessageHandler()
        {
            throw new NotImplementedException();
        }

        [FunctionName("AzureStorageNotifier_Start")]
        public static async Task HttpBlobStart(
        [BlobTrigger("samples-workitems/{name}", Connection = "StorageConnectionString")] CloudBlockBlob myCloudBlob, string name, ILogger log,
        [DurableClient] IDurableOrchestrationClient starter)
        {
            try
            {

                log.LogInformation($"Started orchestration trigged by BLOB trigger. A blob item with name = '{name}'");
                log.LogInformation($"BLOB Name {myCloudBlob.Name}");

                // Function input comes from the request content.
                if (myCloudBlob != null)
                {
                    var newUploadedBlobItem = new CloudBlobItem
                    {
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
            catch (Exception)
            {

                throw;
            }
        }


        static async Task ServiceBusQueueMessageHandler(ProcessMessageEventArgs args)
        {         

            string queueMessageBody = args.Message.Body.ToString();
            if (String.IsNullOrEmpty(queueMessageBody))
            {
                queueMessages.Add(queueMessageBody);
                Console.WriteLine($"Received Message Service Bus Queue: {queueMessageBody}");
            }           

            //Complete message processing. Message deleted from queue
            await args.CompleteMessageAsync(args.Message);

            Console.WriteLine($"Message handler queue message reading completed");
        }


        public static Task ServiceBusQueueMessageErrorHandler(ProcessErrorEventArgs args)
        {

            Console.WriteLine($"Error getting message from Service Bus Queue: {args.Exception.ToString()}");
            return Task.CompletedTask;          
        }

    }
}