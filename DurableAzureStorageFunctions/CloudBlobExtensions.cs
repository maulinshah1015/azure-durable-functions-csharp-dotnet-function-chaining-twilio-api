using Microsoft.Azure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DurableAzureStorageFunctions
{
    public static class CloudBlobExtensions
    {
        public static async Task SerializeObjectToBlobAsync(this CloudBlockBlob blob, object obj)
        {
            using (Stream stream = await blob.OpenWriteAsync())
            using (StreamWriter sw = new StreamWriter(stream))
            using (JsonTextWriter jtw = new JsonTextWriter(sw))
            {
                JsonSerializer ser = new JsonSerializer();
                ser.Serialize(jtw, obj);
            }
        }

        public static async Task<T> DeserializeObjectFromBlobAsync<T>(this CloudBlockBlob blob)
        {
            using (Stream stream = await blob.OpenReadAsync())
            using (StreamReader sr = new StreamReader(stream))
            using (JsonTextReader jtr = new JsonTextReader(sr))
            {
                JsonSerializer ser = new JsonSerializer();
                return ser.Deserialize<T>(jtr);
            }
        }
    }
}
