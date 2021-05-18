﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DurableAzureStorageFunctions
{
    public class CloudBlobItem
    {
        public CloudBlobItem()
        {

        }
       
        public string Name { get; set; }
        public string Size { get; set; }

        public string FileType { get; set; }

        public string FileSize { get; set; }

        public string BlobUrl { get; set; }

        public string ETag { get; set; }

        public Dictionary<string, string> Metadata { get; set; }

    }
}
