using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace DurableAzureStorageFunctions
{
    /// <summary>
    /// Azure Durable Functions FUNCTION CHAINING EXAMPLE trigged 
    /// by Azure Functions BLOB Trigger and processed further with other
    /// activity methods like sending SMS or Call to notify user 
    /// about the Azure Service Bus Queue message update.
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

                //TODO Chain #3 Get messages from Azure Service Bus Queue after admin gets sms 
                var isMessageReceivedFromQueueAndForwarded = await context.CallActivityAsync<bool>("AzureStorageNotifier_ReceiveUploadedBlobFromServiceBusQueue", isSmsSentAndCalledUser);

            
                return $"A new cloud blob named {uploadedCloudBlob.Name} was uploaded to Azure Storage " +
                    $"and added to service bus queue. " +
                    $"SMS sent = {isSmsSentAndCalledUser} to assigned user." +
                    $" Access via BLOB URL: {uploadedCloudBlob.BlobUrl}" +
                    $"and processed queue messages was sent to receipients email = {isMessageReceivedFromQueueAndForwarded}" ;
            }
            catch (Exception)
            {
                //TODO Handle possible errors 
                throw;
            }           
        }
       
        [FunctionName("AzureStorageNotifier_SendMessageToServiceBusQueue")]
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
            var twilioVerifiedNumber = azureServiceBusConfig["Twilio_Verified_Number"];

            TwilioClient.Init(twilioAccountId, twilioSecret);

            try
            {
                if (isAlreadySavedToQueue == true)
                {
                    //Send SMS to Azure Service Bus Admin User
                    var smsMessage = await MessageResource.CreateAsync(
                       body: "Hi Admin! A new cloud blob file was uploaded to your Service Bus Queue.",
                       from: new PhoneNumber(twilioVerifiedNumber),
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
                    from: new PhoneNumber(twilioVerifiedNumber),
                    to: new PhoneNumber(twilioAdminMobile)
                    );

                    //Backend logging
                    log.LogInformation($"Called admin on number {twilioAdminMobile}. \n " +
                                      $"Call Id : {call.Id} \n " +
                                      $"Call Status : {call.Status} \n " +
                                      $"Call Completed : {call.IsCompleted} \n ");            

                    return true;
                }else return false;

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
                    log.LogInformation($"Azure Service Bus Queue was updated with new uploaded BLOB. Receiving uploaded data from Azure Service Bus");

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

                        //Manually stop receiving message from Service Bus Queue if error occurs
                        await serviceBusProcessor.StopProcessingAsync();
                        return true;
                    }
                }
                else
                {
                    log.LogInformation($"Azure Service Bus Queue is not updated with new data");
                    return false;
                }                      
            }
            catch (Exception ex)            {
               
               
                log.LogInformation($"Something went wrong. Exception : {ex.InnerException}. Stopping process of receiving messages");
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
                //Errorhandling 
                throw;
            }
        }

        public static async Task ServiceBusQueueMessageHandler(ProcessMessageEventArgs args)
        {         

            string queueMessageBody = args.Message.Body.ToString();
          
            Console.WriteLine($"Received Message Service Bus Queue: {queueMessageBody}");
            await SendReceivedQueueMessageAsEmail(queueMessageBody);
            await args.CompleteMessageAsync(args.Message);

            // bool isReceivedQueueMessageSent = await SendReceivedQueueMessageAsEmail(queueMessageBody);
            //if (!string.IsNullOrEmpty(queueMessageBody))
            //{               
            //    Console.WriteLine($"Received Message Service Bus Queue: {queueMessageBody}");
            //    bool isReceivedQueueMessageSent = await SendReceivedQueueMessageAsEmail(queueMessageBody);

            //    if (isReceivedQueueMessageSent)
            //    {
            //        //Complete message processing. Message deleted from queue

            //    }
            //}           
        }

        public static async Task<bool> SendReceivedQueueMessageAsEmail(string queueMessageBody)
        {

        
            var apiKey = ConfigurationManager.AppSettings["SendGridAPIKey"];
            var adminEmail = ConfigurationManager.AppSettings["Admin_Email"]; 
            var adminName = ConfigurationManager.AppSettings["Admin_Name"]; 
            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(adminEmail, adminName);

            List<EmailAddress> recipients = new List<EmailAddress>
              {
                  new EmailAddress("jonah@jonahandersson.tech", "Jonah @JonahAndersson Tech"),
                  new EmailAddress("jonah.andersson@forefront.se", "Jonah @Forefront")
              };

            var subject = "Hello world email from Sendgrid ";
            var htmlContent = @"<strong>" + queueMessageBody + "</strong>";
            var displayRecipients = false; // set this to true if you want recipients to see each others mail id 
            var msg = MailHelper.CreateSingleEmailToMultipleRecipients(from, recipients, subject, "", htmlContent, displayRecipients);
            var response = await client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
                return true;
            else return false;
        }

        public static Task ServiceBusQueueMessageErrorHandler(ProcessErrorEventArgs args)
        {

            Console.WriteLine($"Error getting message from Service Bus Queue: {args.Exception.ToString()}");
            return Task.CompletedTask;          
        }

    }
}