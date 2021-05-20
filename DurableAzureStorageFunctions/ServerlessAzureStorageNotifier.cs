using System;
using System.Collections.Generic;
using System.IO;
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
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace DurableAzureStorageFunctions
{
    /// <summary>
    /// Azure Durable Functions FUNCTION CHAINING EXAMPLE trigged by BLOB Trigger
    /// </summary>
    public static class ServerlessAzureStorageNotifier
    {     

        [FunctionName("AzureStorageNotifier_Orchestrator")]
        public static async Task<string> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            try
            {
                var uploadedCloudBlob = context.GetInput<CloudBlobItem>();               

                //Chain #1 Send Message with BLOB details to Service Bus Queue
                var isMessageSentToServiceBusQueue = await context.CallActivityAsync<bool>("AzureStorageNotifier_SendMessageToServiceBusQueue", uploadedCloudBlob);

                //Chain #2 Send SMS and call notification to set admin user that queue was updated with new blob
                var isSmsSentAndCalledUser = await context.CallActivityAsync<bool>("AzureStorageNotifier_SendSMSAndCallToUserViaTwilioAPI", isMessageSentToServiceBusQueue);

                //TODO Chain #3 Get messages from Queue after admin gets sms 
                //var isMessageReceivedFromQueueAndForwarded = await context.CallActivityAsync<bool>("AzureStorageNotifier_ReceiveUploadedBlobFromServiceBusQueue", isSmsSent);
                
                return $"A new cloud blob named {uploadedCloudBlob.Name} was uploaded to Azure Storage " +
                    $"and added to service bus queue. " +
                    $"SMS sent = {isSmsSentAndCalledUser} to assigned user." +
                    $" Access via BLOB URL: {uploadedCloudBlob.BlobUrl}" +
                    $"and process the queue messages" ;

            }
            catch (Exception)
            {
                //TODO Handle possible errors 
                throw;
            }           
        }
       
        [FunctionName("AzureStorageNotifier_SendMessageToServiceBusQueue")]
        //[return: ServiceBus("azdurablefunctioncloudqueue", EntityType.Queue, Connection = "AzureServiceBusConnectionString")]
        public static async Task<bool> SendMessageToAzureServiceBusQueueAsync([ActivityTrigger] CloudBlobItem uploadedcloudBlob, ILogger log, ExecutionContext executionContext)
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

            try
            {

                if (uploadedcloudBlob != null)
                {
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
                else                
                    return false;

                
            }
            catch (Exception ex)
            {
                log.LogInformation($"Something went wrong sending the message to the queue : {serviceBusQueue}. Exception {ex.InnerException}" );
                throw;
            }
        }


        [FunctionName("AzureStorageNotifier_SendSMSAndCallToUserViaTwilioAPI")]
        public static async Task<bool> SendSMSCallMessageTwilio([ActivityTrigger] bool isAlreadySavedToQueue, ILogger log, ExecutionContext executionContext)
        {
            log.LogInformation($"BLOB already saved to queue.");
            //Config settings for Azure Service Bus
            var azureServiceBusConfig = new ConfigurationBuilder()
                 .SetBasePath(executionContext.FunctionAppDirectory)
                 .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                 .AddEnvironmentVariables()
                 .Build();

            var twilioAccountId = azureServiceBusConfig["Twilio_SID"];
            var twilioSecret = azureServiceBusConfig["Twilio_Secret"];
            var twilioAdminMobile = azureServiceBusConfig["Admin_Mobile"];

            TwilioClient.Init(twilioAccountId, twilioSecret);

            try
            {
                if (isAlreadySavedToQueue == true)
                {
                    //Send SMS to Azure Service Bus Admin User
                    var smsMessage = await MessageResource.CreateAsync(
                       body: "Hi Admin! A new cloud blob file was uploaded to your Service Bus Queue.",
                       from: new PhoneNumber("+18435075357"),
                       to: new PhoneNumber(twilioAdminMobile)
                     );

                    //Backend logging
                    log.LogInformation($"Sms sent to the number {twilioAdminMobile}. \n " +
                                        $"Message Id : {smsMessage.Sid} \n " +                                       
                                        $"Date Sent : {smsMessage.DateSent} \n " +
                                        $"Message : {smsMessage.Body}");

                    //Initiate call reminder to admin
                    var call = CallResource.CreateAsync(
                    twiml: new Twiml("<Response><Say>Hi Jonah! Call reminder. New BLOB added to Service Bus Queue!</Say></Response>"),                   
                    from: new PhoneNumber("Your verified TwilioNumber"),
                    to: new PhoneNumber(twilioAdminMobile)
                    );

                    //Backend logging
                    log.LogInformation($"Called admin on number {twilioAdminMobile}. \n " +
                                      $"Call Id : {call.Id} \n " +
                                      $"Call Status : {call.Status} \n " +
                                      $"Call Completed : {call.IsCompleted} \n ");            

                    return true;
                }else
                    return false;

            }
            catch (ApiException e)
            {
                if (e.Code == 21614)
                {
                    Console.WriteLine("Uh oh, looks like this caller can't receive SMS messages.");
                }
                throw;              
            }
        }
    


        [FunctionName("AzureStorageNotifier_ReceiveUploadedBlobFromServiceBusQueue")]
        public static async Task<bool> ReceiveUploadedBlobFromServiceBusQueue([ActivityTrigger] bool isQueueMessageSent, ILogger log, ExecutionContext executionContext)
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

            try
            {

                if (isQueueMessageSent == true)
                {
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
                        //Console.WriteLine($"Processing wait for a minute. Process any key to end processing");
                        //Console.ReadKey();

                        //TODO 
                        //Stop manually processing 
                        await serviceBusProcessor.StopProcessingAsync();
                        //Console.WriteLine($"Stopped receiving messages");
                        return true;
                    }
                }
                else
                {
                 
                    return false;
                }
                      
            }
            catch (Exception)
            {
                //Error handling
               
                throw;
            }

             
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

        private static async Task ServiceBusQueueMessageHandler(ProcessMessageEventArgs args)
        {         

            string queueMessageBody = args.Message.Body.ToString();
            if (String.IsNullOrEmpty(queueMessageBody))
            {               
                Console.WriteLine($"Received Message Service Bus Queue: {queueMessageBody}");
                SaveMessageToTextFile(queueMessageBody);
            }           

            //Complete message processing. Message deleted from queue
            await args.CompleteMessageAsync(args.Message);           

        }

        public static void SaveMessageToTextFile(string queueMessageBody)
        {
            try
            {
                // string pathToOutputFileFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Data\outputGreetings.txt");
                string pathToOutputFileFile = @"C:\Users\jonah.andersson\Google Drive\Jonah-DevProjects\azure-durable-functions-csharp-dotnet-function-chaining-twilio-api\DurableAzureStorageFunctions\Data\outputdata.txt";
                if (!String.IsNullOrEmpty(queueMessageBody))
                {
                    // This text is added only once to the file.
                    if (!File.Exists(pathToOutputFileFile))
                    {
                        // Create a file to write to.
                        using (StreamWriter sw = File.CreateText(pathToOutputFileFile))
                        {
                            sw.WriteLine(queueMessageBody);
                        }
                    }
                    else
                    {
                        using (StreamWriter sw = File.CreateText(pathToOutputFileFile))
                        {
                            sw.WriteLine(queueMessageBody);

                        }
                    }
                }

            }
            catch (Exception)
            {
                //TODO : Handle errors and exceptions 
                throw;
            }
        }

        public static Task ServiceBusQueueMessageErrorHandler(ProcessErrorEventArgs args)
        {

            Console.WriteLine($"Error getting message from Service Bus Queue: {args.Exception.ToString()}");
            return Task.CompletedTask;          
        }

    }
}