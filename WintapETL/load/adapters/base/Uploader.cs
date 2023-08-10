using ChoETL;
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
    }
}
