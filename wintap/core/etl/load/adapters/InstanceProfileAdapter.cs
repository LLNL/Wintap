using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using gov.llnl.wintap.core.etl.load.adapters.baseclass;
using gov.llnl.wintap.core.etl.load.interfaces;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.IO;

namespace gov.llnl.wintap.core.etl.load.adapters
{
    internal class InstanceProfileAdapter : Uploader, IUpload
    {
        private InstanceProfileAWSCredentials instanceProfileAWSCredentials;
        private AmazonS3Client client;

        public event EventHandler<string> UploadCompleted;

        public bool PostUpload()
        {
            WintapLogger.Log.Append(this.Name + " post upload method called", LogLevel.Always);
            client.Dispose();
            this.stopSessionStats();
            return true;
        }

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            WintapLogger.Log.Append(" PreUpload method called", LogLevel.Always);
            instanceProfileAWSCredentials = new InstanceProfileAWSCredentials();            
            client = new AmazonS3Client(instanceProfileAWSCredentials, Amazon.RegionEndpoint.GetBySystemName(parameters["RegionEndpoint"]));
            this.startSessionStats();
            WintapLogger.Log.Append(" PreUpload method complete", LogLevel.Always);
            return true;
        }

        public bool Upload(string localFile, Dictionary<string, string> parameters)
        {
            WintapLogger.Log.Append(this.Name + " upload method called", LogLevel.Always);
            bool fileSent = false;
            PutObjectRequest req = new PutObjectRequest();
            req.BucketName = parameters["Bucket"];
            WintapLogger.Log.Append("Bucket: " + req.BucketName, LogLevel.Always);
            
            FileInfo localFileInfo = new FileInfo(localFile);
            string objectKey = getS3ObjectNameForFile(localFileInfo.Name);
            WintapLogger.Log.Append("s3 object key: " + objectKey, LogLevel.Always);
            if (req.BucketName != "NONE")
            {
                req.Key = objectKey;
                WintapLogger.Log.Append("attempting S3 upload: " + req.Key, LogLevel.Always);
                req.FilePath = localFile;
                req.Metadata.Add("ComputerName", Environment.MachineName);
                req.Metadata.Add("Timestamp", DateTime.Now.ToFileTimeUtc().ToString());
                req.CannedACL = S3CannedACL.BucketOwnerFullControl;
                //PutObjectResponse resp = await client.PutObjectAsync(req);
                fileSent = true;
                //WintapLogger.Log.Append("  upload http status code:  " + resp.HttpStatusCode, LogLevel.Always);
            }
            else
            {
                throw new Exception("NO_BUCKET_SPECIFIED");
            }
            return fileSent;
        }
    }
}
