using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using gov.llnl.wintap.etl.load.adapters.baseclass;
using gov.llnl.wintap.etl.load.interfaces;
using gov.llnl.wintap.etl.shared;
using Newtonsoft.Json.Linq;
using Parquet.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace gov.llnl.wintap.etl.load.adapters
{
    internal class InstanceProfileAdapter : Uploader, IUpload
    {
        private InstanceProfileAWSCredentials instanceProfileAWSCredentials;
        private AmazonS3Client client;

        public event EventHandler<string> UploadCompleted;

        public bool PostUpload()
        {
            Logger.Log.Append(this.Name + " post upload method called", LogLevel.Always);
            client.Dispose();
            this.stopSessionStats();
            return true;
        }

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            Logger.Log.Append(" PreUpload method called", LogLevel.Always);
            instanceProfileAWSCredentials = new InstanceProfileAWSCredentials();            
            client = new AmazonS3Client(instanceProfileAWSCredentials, Amazon.RegionEndpoint.GetBySystemName(parameters["RegionEndpoint"]));
            this.startSessionStats();
            Logger.Log.Append(" PreUpload method complete", LogLevel.Always);
            return true;
        }

        public bool Upload(string localFile, Dictionary<string, string> parameters)
        {
            Logger.Log.Append(this.Name + " upload method called", LogLevel.Always);
            bool fileSent = false;
            PutObjectRequest req = new PutObjectRequest();
            req.BucketName = parameters["Bucket"];
            Logger.Log.Append("Bucket: " + req.BucketName, LogLevel.Always);
            
            FileInfo localFileInfo = new FileInfo(localFile);
            string objectKey = getS3ObjectNameForFile(localFileInfo.Name);
            Logger.Log.Append("s3 object key: " + objectKey, LogLevel.Always);
            if (req.BucketName != "NONE")
            {
                req.Key = objectKey;
                Logger.Log.Append("attempting S3 upload: " + req.Key, LogLevel.Always);
                req.FilePath = localFile;
                req.Metadata.Add("ComputerName", Environment.MachineName);
                req.Metadata.Add("Timestamp", DateTime.Now.ToFileTimeUtc().ToString());
                req.CannedACL = S3CannedACL.BucketOwnerFullControl;
                PutObjectResponse resp = client.PutObject(req);
                fileSent = true;
                Logger.Log.Append("  upload http status code:  " + resp.HttpStatusCode, LogLevel.Always);
            }
            else
            {
                throw new Exception("NO_BUCKET_SPECIFIED");
            }
            return fileSent;
        }
    }
}
