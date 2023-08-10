using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gov.llnl.wintap.etl.model
{
    public class ETLConfig
    {
        public ETLConfig() { }
        public string LogLevel { get; set; }
        public string SensorProfile { get; set; }
        public int SerializationIntervalSec { get; set; }
        public int UploadIntervalSec { get; set; }
        public bool WriteToParquet { get; set; }
        public bool WriteToCsv { get; set; }
        public List<Uploader> Uploaders = new List<Uploader>();
        public class Uploader
        {
            public Uploader()
            {
                Properties = new Dictionary<string, string>();
            }
            public string Name { get; set; }
            public bool Enabled { get; set; }
            public Dictionary<string, string> Properties;
        }
    }
}
