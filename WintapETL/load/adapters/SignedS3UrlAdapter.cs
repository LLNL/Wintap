using gov.llnl.wintap.etl.load.adapters.baseclass;
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

namespace gov.llnl.wintap.etl.load.adapters
{
    internal class SignedS3UrlAdapter : Uploader, IUpload
    {
        private bool processing;
        private MqttClient client;
        private string clientId;
        private CertificateManager certificateManager;

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
            Logger.Log.Append("PreUpload method called on SignedS3UrlUploader", LogLevel.Always);
            certificateManager = new CertificateManager(parameters["CertificateStore"], parameters["DeviceCertificateName"]);
            Logger.Log.Append("Got certs: " + certificateManager.deviceCertificate.SubjectName.ToString(), LogLevel.Always);

            // Create a new MQTT client.
            client = new MqttClient(parameters["EndPoint"], Convert.ToInt32(parameters["Port"]), true, null, certificateManager.deviceCertificate, MqttSslProtocols.TLSv1_2);
            
            //Event Handler Wiring
            client.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;
            client.MqttMsgSubscribed += Client_MqttMsgSubscribed;

            clientId = Environment.MachineName.ToLower();

            //Connect
            client.Connect(clientId);
            Logger.Log.Append($"Connected to AWS IoT with client id: " + clientId, LogLevel.Always);

            //subscribe to response
            string topic = "wintap/" + clientId + "/response";
            client.Subscribe(new string[] { topic }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE });
            Logger.Log.Append("IoT subscription set", LogLevel.Always);


            return true;
        }

        public bool Upload(string localFile, Dictionary<string,string> parameters)
        {
            processing = true;
            try
            {
                // convert local path to s3 object path
                string s3Path = getS3ObjectNameForFile(localFile);

                //send url request
                var message = JsonConvert.SerializeObject(new IotMessage() { filename = localFile, s3objectpath = s3Path });
                client.Publish("wintap/" + clientId + "/request", Encoding.UTF8.GetBytes($"{message}"));
                Logger.Log.Append("published signed url request to topic: " + "wintap/" + clientId + "/request", LogLevel.Always);
            }
            catch (Exception ex)
            {
                Logger.Log.Append("MQTT error: " + ex.Message, LogLevel.Always);
            }
            return true;
        }

        public bool PostUpload()
        {
            Logger.Log.Append("PostUpload method called on SignedS3UrlUploader", LogLevel.Always);
            client.Disconnect();
            return true;
        }

        private void Client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            Logger.Log.Append("Subscribed to topic", LogLevel.Always);
        }

        private void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            IotMessage iotMsg = JsonConvert.DeserializeObject<IotMessage>(Encoding.UTF8.GetString(e.Message));
            Logger.Log.Append("Filename: " + iotMsg.filename + "   URL: " + iotMsg.url, LogLevel.Always);
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
                    if (response.IsSuccessStatusCode)
                    {
                        Logger.Log.Append("    response: " + response.StatusCode.ToString(), LogLevel.Always);
                    }
                    else
                    {
                        Logger.Log.Append("    FAIL: " + response.StatusCode.ToString(), LogLevel.Always);
                    }
                }
            }
            catch(Exception ex)
            {
                Logger.Log.Append("ERROR in upload: " + ex.Message, LogLevel.Always);
            }
        }
    }

    public class IotMessage
    {
        public string s3objectpath { get; set; }
        public string filename { get; set; }
        public string url { get; set; }
    }
}
