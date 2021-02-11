// An HTTP trigger Azure Function that returns a SAS token for Azure Storage for the specified container. 
// You can also optionally specify a particular blob name and access permissions. 

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

// Request body format: 
// - `container` - *required*. Name of container in storage account
// - `blobName` - *required*. Used to scope permissions to a particular blob/file
// - `permissions` - *optional*. Default value is read permissions. The format matches the enum values of SharedAccessBlobPermissions. 
//                               Possible values are "Read", "Write", "Delete", "List", "Add", "Create". Comma-separate multiple permissions, such as "Read, Write, Create".
// - `time` - *required*. SAS expiry time in minutes 

namespace SASGenerator.Function
{
    public static class HttpTriggerCSharp
    {
        [FunctionName("HttpTriggerCSharp")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            
            // Parse `requestBody` to get back JSON payload in `data`
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            
            // Parse`container`
            if (data.container == null) {
                return new BadRequestObjectResult("Specify value for 'container'");
            }

            // Parse `permissions`
            var permissions = SharedAccessBlobPermissions.Read; // default to read permissions
            bool success = Enum.TryParse(data.permissions.ToString(), out permissions);

            if (!success) {
                return new BadRequestObjectResult("Invalid value for 'permissions'");
            }
            
            // Parse `time`
            if (data.time == null) {
                return new BadRequestObjectResult("Specify value for 'time'");
            }

            // Initiate connection to Storage Account
            var storageAccount = CloudStorageAccount.Parse(System.Environment.GetEnvironmentVariable("AzureWebJobsStorage", EnvironmentVariableTarget.Process));
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(data.container.ToString());

            // Generate SAS Token
            var sasToken = GetBlobSasToken(container, data.blobName.ToString(), Int32.Parse(data.time.ToString()), permissions);

            // Return SAS URI with Custom Domain
            var customDomain = System.Environment.GetEnvironmentVariable("Custom_Domain", EnvironmentVariableTarget.Process);

            string responseMessage = $"https://{customDomain}/{data.container}/{data.blobName}{sasToken}";

            return new OkObjectResult(responseMessage);
        }
        public static string GetBlobSasToken(CloudBlobContainer container, string blobName, int time, SharedAccessBlobPermissions permissions, string policyName = null)
        {
            string sasBlobToken;

            // Get a reference to a blob within the container.
            // Note that the blob may not exist yet, but a SAS can still be created for it.
            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);

            if (policyName == null) {
                var adHocSas = CreateAdHocSasPolicy(permissions, time);

                // Generate the shared access signature on the blob, setting the constraints directly on the signature.
                sasBlobToken = blob.GetSharedAccessSignature(adHocSas);
            }
            else {
                // Generate the shared access signature on the blob. In this case, all of the constraints for the
                // shared access signature are specified on the container's stored access policy.
                sasBlobToken = blob.GetSharedAccessSignature(null, policyName);
            } 

            return sasBlobToken;
        }
        private static SharedAccessBlobPolicy CreateAdHocSasPolicy(SharedAccessBlobPermissions permissions, int time)
        {
            // Create a new access policy and define its constraints.
            // Note that the SharedAccessBlobPolicy class is used both to define the parameters of an ad-hoc SAS, and 
            // to construct a shared access policy that is saved to the container's shared access policies. 

            return new SharedAccessBlobPolicy() {
                // Set start time to five minutes before now to avoid clock skew.
                SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-5),
                SharedAccessExpiryTime = DateTime.UtcNow.AddMinutes(time),
                Permissions = permissions
            };
        }
    }
}
