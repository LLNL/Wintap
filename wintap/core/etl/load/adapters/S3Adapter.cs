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
    internal class S3Adapter : Uploader, IUpload
    {
        private AWSCredentials awsCredentials;
        private AmazonS3Client client;

        public event EventHandler<string> UploadCompleted;

        public bool PostUpload()
        {
            WintapLogger.Log.Append(this.Name + " post upload method called", LogLevel.Info);
            client?.Dispose();
            client = null;
            this.stopSessionStats();
            return true;
        }

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            WintapLogger.Log.Append(" PreUpload method called", LogLevel.Info);

            awsCredentials = createCredentials(parameters);
            AmazonS3Config s3Config = createS3Config(parameters);
            client = new AmazonS3Client(awsCredentials, s3Config);

            this.startSessionStats();
            WintapLogger.Log.Append(" PreUpload method complete", LogLevel.Info);
            return true;
        }

        public async Task<bool> Upload(string localFile, Dictionary<string, string> parameters)
        {
            WintapLogger.Log.Append(this.Name + " upload method called", LogLevel.Info);
            bool fileSent = false;

            string bucketName = getParameter(parameters, "Bucket");
            if (string.IsNullOrWhiteSpace(bucketName))
            {
                throw new Exception("NO_BUCKET_SPECIFIED");
            }

            WintapLogger.Log.Append("Bucket: " + bucketName, LogLevel.Info);

            FileInfo localFileInfo = new FileInfo(localFile);
            if (!localFileInfo.Exists)
            {
                throw new FileNotFoundException("Local file does not exist", localFile);
            }

            string objectKey = getS3ObjectNameForFile(localFileInfo.FullName, parameters);
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
                    updateSessionStats();
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

        private AWSCredentials createCredentials(Dictionary<string, string> parameters)
        {
            string accessKey = getParameter(parameters, "AccessKey");
            string secretKey = getParameter(parameters, "SecretKey");
            string sessionToken = getParameter(parameters, "SessionToken");

            if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
            {
                WintapLogger.Log.Append("Using S3 credentials from ETLConfig AccessKey/SecretKey", LogLevel.Info);
                if (!string.IsNullOrWhiteSpace(sessionToken))
                {
                    return new SessionAWSCredentials(accessKey, secretKey, sessionToken);
                }

                return new BasicAWSCredentials(accessKey, secretKey);
            }

            WintapLogger.Log.Append("Using S3 instance profile credentials", LogLevel.Info);
            return new InstanceProfileAWSCredentials();
        }

        private AmazonS3Config createS3Config(Dictionary<string, string> parameters)
        {
            string regionEndpoint = getParameter(parameters, "RegionEndpoint");
            string serviceUrl = getParameter(parameters, "ServiceURL");
            if (string.IsNullOrWhiteSpace(serviceUrl))
            {
                serviceUrl = getParameter(parameters, "Endpoint");
            }

            AmazonS3Config config = new AmazonS3Config();

            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                config.ServiceURL = serviceUrl;
                WintapLogger.Log.Append("Using S3 service URL: " + serviceUrl, LogLevel.Info);

                if (!string.IsNullOrWhiteSpace(regionEndpoint))
                {
                    config.AuthenticationRegion = regionEndpoint;
                }
            }
            else if (!string.IsNullOrWhiteSpace(regionEndpoint))
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(regionEndpoint);
                WintapLogger.Log.Append("Using S3 region endpoint: " + regionEndpoint, LogLevel.Info);
            }

            if (bool.TryParse(getParameter(parameters, "ForcePathStyle"), out bool forcePathStyle))
            {
                config.ForcePathStyle = forcePathStyle;
            }

            return config;
        }
    }
}
