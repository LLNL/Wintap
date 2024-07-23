using gov.llnl.wintap.etl.shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.etl.load.adapters.baseclass
{
    internal class Uploader
    {
        protected int counter;
        protected Stopwatch watch;

        public string Name { get; set; }

        public Uploader()
        {
            counter = 0;
            watch = new Stopwatch();
        }

        protected void startSessionStats()
        {
            watch.Start();
        }

        protected void updateSessionStats()
        {
            counter++;
        }

        protected void stopSessionStats()
        {
            this.watch.Stop();
            Logger.Log.Append("Uploader: " + this.Name + " uploaded " + counter + " files in " + watch.Elapsed.TotalSeconds + " seconds", LogLevel.Always);
            this.watch.Reset();
            this.counter = 0;
        }

        /// <summary>
        /// converts parquet file name to s3 object name
        /// </summary>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        protected string getS3ObjectNameForFile(string dataFile)
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
    }
}
