using Amazon.S3;
using gov.llnl.wintap.core.etl.load.adapters.baseclass;
using gov.llnl.wintap.core.etl.load.interfaces;
using gov.llnl.wintap.core.infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.core.etl.load.adapters
{
    internal class SMBFileShareAdapter : Uploader, IUpload
    {
        private Uri uncPath;

        public event EventHandler<string> UploadCompleted;

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
                if (!uncPath.IsUnc)
                {
                    throw new Exception();
                }
            }
            catch (Exception ex)
            {
                preUploadSuccess = false;
                WintapLogger.Log.Append("Error in " + this.Name + "   could not parse UNCPath from config.  Verify the value is defined and is a parseable UNC path", LogLevel.Info);
            }
            this.startSessionStats();
            return preUploadSuccess;
        }

        public async Task<bool> Upload(string localFile, Dictionary<string, string> parameters)
        {
            bool uploadSuccess = true;
            try
            {
                FileInfo fileInfo = new FileInfo(localFile);
                string relativePath = getFileShareRelativePathForFile(fileInfo.FullName, parameters);
                string destinationFile = Path.Combine(uncPath.LocalPath, relativePath);
                string destinationDirectory = Path.GetDirectoryName(destinationFile);

                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                fileInfo.CopyTo(destinationFile, true);
                this.updateSessionStats();
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("Error uploading file: " + ex.Message, LogLevel.Info);
                uploadSuccess = false;
            }
            return uploadSuccess;
        }
    }
}
