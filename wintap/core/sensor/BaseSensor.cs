namespace gov.llnl.wintap.core.collect
{
    public abstract class BaseSensor
    {

        /// <summary>
        /// The name of the thing that generates the events this instance collects. 
        /// For ETW sources, you may want to use the ProviderName or the EventName fields.
        /// </summary>
        internal string SensorName { get; set; }

        public virtual bool Start()
        {
            return true;
        }

        public virtual void Stop()
        {

        }

        internal BaseSensor()
        {

        }

    }
}
