﻿using gov.llnl.wintap.etl.load.adapters.baseclass;
using gov.llnl.wintap.etl.load.interfaces;
using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using System.Net.Http;
using Newtonsoft.Json;
using gov.llnl.wintap.etl.models;
using System.Web.UI.WebControls;
using System.ComponentModel;
using System.Diagnostics;

namespace gov.llnl.wintap.etl.load.adapters
{
    internal class SignedS3UrlAdapter : Uploader, IUpload
    {
        private bool processing;
        private MqttClient client;
        private string clientId;
        private CertificateManager certificateManager;
        private int pendingUploadCounter;

        public event EventHandler<string> UploadCompleted;
        protected virtual void OnUploadCompleted(string message)
        {
            EventHandler<string> handler = UploadCompleted;
            if (handler != null)
            {
                handler(this, message);
            }
        }

        public string Name { get; set; }

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            Logger.Log.Append("Initiating MQTT session", LogLevel.Always);
            bool preUploadSuccess = false;
            try
            {
                pendingUploadCounter = 0;
                certificateManager = new CertificateManager(parameters["CertificateStore"], parameters["DeviceCertificateName"]);
                Logger.Log.Append("Connecting with subject: " + certificateManager.deviceCertificate.Subject, LogLevel.Always);

                client = new MqttClient(parameters["EndPoint"], Convert.ToInt32(parameters["Port"]), true, null, certificateManager.deviceCertificate, MqttSslProtocols.TLSv1_2);

                client.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;
                client.MqttMsgSubscribed += Client_MqttMsgSubscribed;

                clientId = Environment.MachineName.ToLower();

                Logger.Log.Append("Attempting MQTT client connect with clientId: " + clientId, LogLevel.Always);
                client.Connect(clientId);
                Logger.Log.Append("   Connected.  creating topic subscription.", LogLevel.Always);

                string topic = "wintap/" + clientId + "/response";
                client.Subscribe(new string[] { topic }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE });
                preUploadSuccess = true;
                Logger.Log.Append("MQTT session created.", LogLevel.Always);
            }
            catch(Exception ex)
            {
                Logger.Log.Append("ERROR in PreUpload: " + ex.Message, LogLevel.Always);
            }
            return preUploadSuccess;
        }

        public bool Upload(string localFile, Dictionary<string,string> parameters)
        {
            processing = true;
            bool uploadSuccess = false;
            try
            {
                Logger.Log.Append("starting upload on file: " + localFile, LogLevel.Always);
                string s3Path = getS3ObjectNameForFile(localFile);
                Logger.Log.Append("  converted s3 path: " + s3Path, LogLevel.Debug);
                //send url request
                var message = JsonConvert.SerializeObject(new IotMessage() { filename = localFile, s3objectpath = s3Path });
                client.Publish("wintap/" + clientId + "/request", Encoding.UTF8.GetBytes($"{message}"));
                uploadSuccess = true;
                pendingUploadCounter++;
                Logger.Log.Append("signed s3 upload url requested for " + localFile, LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("Upload error: " + ex.Message, LogLevel.Always);
            }
            return uploadSuccess;
        }

        public bool PostUpload()
        {
            Logger.Log.Append("PostUpload method called on SignedS3UrlUploader", LogLevel.Always);
            BackgroundWorker uploadWaitWorker = new BackgroundWorker();
            uploadWaitWorker.WorkerSupportsCancellation = true;
            uploadWaitWorker.DoWork += UploadWaitWorker_DoWork;
            uploadWaitWorker.RunWorkerCompleted += UploadWaitWorker_RunWorkerCompleted;
            uploadWaitWorker.RunWorkerAsync();
            return true;
        }

        private void UploadWaitWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            
        }

        private void UploadWaitWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Stopwatch uploadTimer = Stopwatch.StartNew();
            try
            {
                while(pendingUploadCounter > 0)
                {
                    Logger.Log.Append("Awaiting async upload to complete. Files pending upload: " + pendingUploadCounter, LogLevel.Always);
                    System.Threading.Thread.Sleep(1000);
                    if(uploadTimer.Elapsed.TotalMinutes > 1)
                    {
                        Logger.Log.Append("Timeout execeeded on upload worker.  Files dropped " + pendingUploadCounter, LogLevel.Always);
                        break;
                    }
                }
                Logger.Log.Append("Disconnecting MQTT client.  Total files NOT uploaded: " + pendingUploadCounter, LogLevel.Always);
                client.Disconnect();
            }
            catch (Exception ex)
            {
                Logger.Log.Append("ERROR in postUpload: " + ex.Message, LogLevel.Always);
            }
        }

        private void Client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            Logger.Log.Append("MQTT topic subscription established.", LogLevel.Always);
        }

        private void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            IotMessage iotMsg = JsonConvert.DeserializeObject<IotMessage>(Encoding.UTF8.GetString(e.Message));
            Logger.Log.Append("signed url received for file: " + iotMsg.filename, LogLevel.Debug);
            sendToS3(iotMsg.filename, iotMsg.url);
            // notify cacheManager that this file is ready for pruning.
            OnUploadCompleted(iotMsg.filename);
        }

        private string getS3ObjectNameForFile(string dataFile)
        {
            // S3 folder hierarchy setup
            //   unless tcp/udp, then add one additional layer for efficient filtering
            string objectPrefix = "v3";
            string uploadDPK = DateTime.UtcNow.Year + DateTime.UtcNow.ToString("MM") + DateTime.UtcNow.ToString("dd");
            string uploadHPK = DateTime.UtcNow.ToString("HH");
            string timeSegment = dataFile.Split('+')[2].Split(new char[] { '.' })[0];
            string dataFileEventType = dataFile.Split('+')[1];
            string[] disgardedSuffix = new string[1];
            disgardedSuffix[0] = "_sensor";
            dataFileEventType = dataFileEventType.Split(disgardedSuffix, StringSplitOptions.None)[0];

            long dataFileMergeTime = Int64.Parse(timeSegment);
            DateTime mergeTimeUtc = DateTime.FromFileTimeUtc(dataFileMergeTime);
            long collectTimeAsUnix = ((System.DateTimeOffset)mergeTimeUtc).ToUnixTimeSeconds();
            string objectKey = objectPrefix + "/raw_sensor/" + dataFileEventType + "/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/" + Environment.MachineName.ToLower() + "+" + dataFileEventType + "+" + collectTimeAsUnix + ".parquet";
            if (dataFileEventType == "raw_tcp_process_conn_incr")
            {
                objectKey = objectPrefix + "/raw_sensor/raw_process_conn_incr/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/protoPK=TCP/" + Environment.MachineName.ToLower() + "+" + dataFileEventType + "+" + collectTimeAsUnix + ".parquet";
            }
            else if (dataFileEventType == "raw_udp_process_conn_incr")
            {
                objectKey = objectPrefix + "/raw_sensor/raw_process_conn_incr/uploadedDPK=" + uploadDPK + "/uploadedHPK=" + uploadHPK + "/protoPK=UDP/" + Environment.MachineName.ToLower() + "+" + dataFileEventType + "+" + collectTimeAsUnix + ".parquet";
            }
            return objectKey;
        }

        private async void sendToS3(string localPath, string signedUrl)
        {
            try
            {
                var contentToUpload = new ByteArrayContent(File.ReadAllBytes(localPath));
                using (var httpClient = new HttpClient())
                {
                    Logger.Log.Append("attempting to send: " + localPath + " to : " + signedUrl, LogLevel.Always);
                    var response = await httpClient.PutAsync(signedUrl, contentToUpload);
                    Logger.Log.Append("    HTTP PUT response: " + response.StatusCode.ToString() + " on file: " + localPath, LogLevel.Always);
                }
            }
            catch(Exception ex)
            {
                Logger.Log.Append("ERROR in upload of file: " + localPath + "  msg: " + ex.Message, LogLevel.Always);
            }
            pendingUploadCounter--;
        }
    }

    public class IotMessage
    {
        public string s3objectpath { get; set; }
        public string filename { get; set; }
        public string url { get; set; }
    }
}
