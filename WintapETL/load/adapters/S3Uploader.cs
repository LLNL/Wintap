using Amazon.Runtime;
using Amazon.S3.Model;
using Amazon.S3;
using gov.llnl.wintap.etl.load.interfaces;
using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Antlr4.Runtime.Atn;
using ChoETL;
using gov.llnl.wintap.etl.load.adapters.baseclass;

namespace gov.llnl.wintap.etl.load.adapters
{
    internal class S3Uploader : Uploader, IUpload
    {
        private bool useInstanceProfile;
        private string bucket;
        private string prefix;

        public bool PreUpload(Dictionary<string,string> parameters)
        {
            bool preUploadProcessingStatus = true;
            Logger.Log.Append("PreUpload method called on S3Uploader", LogLevel.Always);
            try
            {
                useInstanceProfile = Boolean.Parse(parameters["UseInstanceProfile"]);
            }
            catch(Exception ex)
            {
                Logger.Log.Append("Error parsing UseInstanceProfile from config.  Check  that the value exists and is a parseable boolean", LogLevel.Always);
                preUploadProcessingStatus = false;
            }

            
            bucket = parameters["Bucket"];
            prefix = parameters["Prefix"];
            this.startSessionStats();
            return preUploadProcessingStatus;
        }

        public bool PostUpload()
        {
            Logger.Log.Append("PostUpload method called on S3Uploader", LogLevel.Always);
            this.stopSessionStats();
            return true;
        }

        public bool Upload(string localFile)
        {
            Logger.Log.Append("Upload method called on S3Uploader", LogLevel.Always);
            sendToS3(new FileInfo(localFile));
            this.updateSessionStats();
            return true;
        }

        private bool sendToS3(FileInfo dataFile)
        {
            // S3 folder hierarchy setup
            // FORMAT:  raw/EventType/uploadedDPK=YYYYMMDD/uploadedHPK=HH/hostname+eventType+timestamp.parquet
            //   unless tcp/udp, then add one additional layer for efficient filtering
            bool fileSent = false;
            string uploadDPK = DateTime.UtcNow.Year + DateTime.UtcNow.ToString("MM") + DateTime.UtcNow.ToString("dd");
            string uploadHPK = DateTime.UtcNow.ToString("HH");
            string timeSegment = dataFile.Name.Split("+")[2].Split(new char[] { '.' })[0];
            string dataFileEventType = dataFile.Name.Split("+")[1];
            string[] disgardedSuffix = new string[1];
            disgardedSuffix[0] = "_sensor";
            dataFileEventType = dataFileEventType.Split(disgardedSuffix, StringSplitOptions.None)[0];

            // rename older event providers for legacy support
            if (dataFileEventType == "raw_processstop") { dataFileEventType = "raw_process_stop"; }
            if (dataFileEventType == "raw_file") { dataFileEventType = "raw_process_file"; }
            if (dataFileEventType == "raw_registry") { dataFileEventType = "raw_process_registry"; }

            long dataFileMergeTime = Int64.Parse(timeSegment);
            DateTime mergeTimeUtc = DateTime.FromFileTimeUtc(dataFileMergeTime);
            long collectTimeAsUnix = ((System.DateTimeOffset)mergeTimeUtc).ToUnixTimeSeconds();
            string objectKey = "/raw_sensor/" + dataFileEventType + "/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/" + Environment.MachineName.ToLower() + "+" + dataFileEventType + "+" + collectTimeAsUnix + ".parquet";
            if (dataFileEventType == "raw_tcp_process_conn_incr")
            {
                objectKey = "/raw_sensor/raw_process_conn_incr/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/protoPK=TCP/" + Environment.MachineName.ToLower() + "+" + dataFileEventType + "+" + collectTimeAsUnix + ".parquet";
            }
            else if (dataFileEventType == "raw_udp_process_conn_incr")
            {
                objectKey = "/raw_sensor/raw_process_conn_incr/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/protoPK=UDP/" + Environment.MachineName.ToLower() + "+" + dataFileEventType + "+" + collectTimeAsUnix + ".parquet";
            }

            AmazonS3Client client = new AmazonS3Client("", "", Amazon.RegionEndpoint.USGovCloudWest1);
            if (useInstanceProfile)
            {
                InstanceProfileAWSCredentials instanceProfileAWSCredentials = new InstanceProfileAWSCredentials();
                client = new AmazonS3Client(instanceProfileAWSCredentials, Amazon.RegionEndpoint.USGovCloudWest1);
            }
            PutObjectRequest req = new PutObjectRequest();
            req.BucketName = bucket;
            if (req.BucketName != "NONE")
            {
                req.Key = prefix + objectKey;
                Logger.Log.Append("attempting S3 upload: " + req.Key, LogLevel.Always);
                req.FilePath = dataFile.FullName;
                req.Metadata.Add("ComputerName", Environment.MachineName);
                req.Metadata.Add("Timestamp", DateTime.Now.ToFileTimeUtc().ToString());
                req.CannedACL = S3CannedACL.BucketOwnerFullControl;
                PutObjectResponse resp = client.PutObject(req);
                fileSent = true;
                Logger.Log.Append("  upload http status code:  " + resp.HttpStatusCode, LogLevel.Always);
                client.Dispose();
            }
            else
            {
                throw new Exception("NO_BUCKET_SPECIFIED");
            }
            return fileSent;
        }
    }
}
