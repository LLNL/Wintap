using Amazon.Runtime.Internal;
using com.espertech.esper.client;
using gov.llnl.wintap.etl.load.adapters.baseclass;
using gov.llnl.wintap.etl.load.interfaces;
using gov.llnl.wintap.etl.shared;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;

namespace gov.llnl.wintap.etl.load.adapters
{
    internal class GatewayAdapter : Uploader, IUpload
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

        public bool PostUpload()
        {
            throw new NotImplementedException();
        }

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            throw new NotImplementedException();
        }

        public bool Upload(string localFile, Dictionary<string, string> parameters)
        {
            //RestClient client = new RestClient();
            //RestRequest request = new RestRequest("https://wintap-ingest-pre.llnl.gov", Method.Post);
            //jsonToSend = "{ \"cardtype\": " + "\"" + cardTypeOCR + "\",\"image\": " + "\"" + base64Image + "\"}";
            //request.AddParameter("application/json; charset=utf-8", jsonToSend, ParameterType.RequestBody);
            //request.RequestFormat = DataFormat.Json;
            //var responseOCR = client.Execute(request);
            return true;
        }
    }


}
