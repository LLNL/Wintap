using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using gov.llnl.wintap.core.etl.load.adapters.baseclass;
using gov.llnl.wintap.core.etl.load.interfaces;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.etl.load.adapters
{
    internal class InstanceProfileAdapter : Uploader, IUpload
    {
        private InstanceProfileAWSCredentials instanceProfileAWSCredentials;
        private AmazonS3Client client;

        public event EventHandler<string> UploadCompleted;

        public bool PostUpload()
        {
            WintapLogger.Log.Append(this.Name + " post upload method called", LogLevel.Info);
            client.Dispose();
            this.stopSessionStats();
            return true;
        }

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            WintapLogger.Log.Append(" PreUpload method called", LogLevel.Info);
            instanceProfileAWSCredentials = new InstanceProfileAWSCredentials();            
            client = new AmazonS3Client(instanceProfileAWSCredentials, Amazon.RegionEndpoint.GetBySystemName(parameters["RegionEndpoint"]));
            this.startSessionStats();
            WintapLogger.Log.Append(" PreUpload method complete", LogLevel.Info);
            return true;
        }

        public async Task<bool> Upload(string localFile, Dictionary<string, string> parameters)
        {
            WintapLogger.Log.Append(this.Name + " upload method called", LogLevel.Info);
            bool fileSent = false;

            if (!parameters.ContainsKey("Bucket"))
            {
                throw new Exception("NO_BUCKET_SPECIFIED");
            }

            string bucketName = parameters["Bucket"];
            WintapLogger.Log.Append("Bucket: " + bucketName, LogLevel.Info);

            FileInfo localFileInfo = new FileInfo(localFile);
            if (!localFileInfo.Exists)
            {
                throw new FileNotFoundException("Local file does not exist", localFile);
            }

            string objectKey = getS3ObjectNameForFile(localFileInfo.Name);
            WintapLogger.Log.Append("s3 object key: " + objectKey, LogLevel.Info);

            if (bucketName != "NONE")
            {
                PutObjectRequest req = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    FilePath = localFile
                };

                req.Metadata.Add("ComputerName", Environment.MachineName);
                req.Metadata.Add("Timestamp", DateTime.Now.ToFileTimeUtc().ToString());
                req.CannedACL = S3CannedACL.BucketOwnerFullControl;

                try
                {
                    PutObjectResponse resp = await client.PutObjectAsync(req);
                    fileSent = true;
                    WintapLogger.Log.Append("Upload HTTP status code: " + resp.HttpStatusCode, LogLevel.Info);
                }
                catch (Exception ex)
                {
                    WintapLogger.Log.Append("Upload failed: " + ex.Message, LogLevel.Info);
                    fileSent = false;
                }
            }

            return fileSent;
        }
    }
}
