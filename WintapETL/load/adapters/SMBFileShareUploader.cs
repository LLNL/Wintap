using Amazon.S3;
using gov.llnl.wintap.etl.load.adapters.baseclass;
using gov.llnl.wintap.etl.load.interfaces;
using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.etl.load.adapters
{
    internal class SMBFileShareUploader : Uploader, IUpload
    {
        private Uri uncPath;

        public bool PostUpload()
        {
            this.stopSessionStats();
            return true;
        }

        public bool PreUpload(Dictionary<string, string> parameters)
        {
            bool preUploadSuccess = true;
            try
            {
                uncPath = new Uri(parameters["UNCPath"]);
                if(!uncPath.IsUnc)
                {
                    throw new Exception();
                }
            }
            catch(Exception ex)
            {
                preUploadSuccess = false;
                Logger.Log.Append("Error in " + this.Name + "   could not parse UNCPath from config.  Verify the value is defined and is a parseable UNC path", LogLevel.Always);
            }
            this.startSessionStats();
            return preUploadSuccess;
        }

        public bool Upload(string localFile)
        {
            bool uploadSuccess = true;
            try
            {
                FileInfo fileInfo = new FileInfo(localFile);
                fileInfo.CopyTo(Path.Combine(uncPath.OriginalString, fileInfo.Name));
                this.updateSessionStats();
            }
            catch(Exception ex)
            {
                Logger.Log.Append("Error uploading file: " + ex.Message, LogLevel.Always);
                uploadSuccess = false;
            }
            return uploadSuccess;
        }
    }
}
