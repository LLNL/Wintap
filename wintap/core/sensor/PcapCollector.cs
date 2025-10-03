using SharpPcap;
using SharpPcap.LibPcap;
using PacketDotNet;
using System;
using System.IO;
using gov.llnl.wintap.core.infrastructure;

namespace gov.llnl.wintap.core.collect
{
    public delegate void PcapEventHandler(object sender, PcapEventArgs e);

    // Custom EventArgs class to hold additional event information.
    public class PcapEventArgs : EventArgs
    {
        public int PacketIndex { get; }
        public string SourceHardwareAddress { get; }
        public string DestinationHardwareAddress { get; }

        public PcapEventArgs(int packetIndex)
        {
            PacketIndex = packetIndex;
        }
    }


    internal class PcapCollector
    {
        private FileInfo pcapInfo;
        private int packetIndex = 0;

        public event PcapEventHandler Emit;

        /// <summary>
        /// Path to the pcap local pcap file
        /// </summary>
        /// <param name="capFile"></param>
        internal PcapCollector(string capFile)
        {
            pcapInfo = new FileInfo(capFile);
        }

        internal void Start()
        {
            ICaptureDevice device;

            try
            {
                // Get an offline device
                device = new CaptureFileReaderDevice(pcapInfo.FullName);

                // Open the device
                device.Open();
            }
            catch (Exception e)
            {
                WintapLogger.Log.Append("Caught exception when opening file" + e.ToString(), LogLevel.Info);
                return;
            }

            // Register our handler function to the 'packet arrival' event
            device.OnPacketArrival += new PacketArrivalEventHandler(device_OnPacketArrival);

            WintapLogger.Log.Append("-- Capturing from '{0}', hit 'Ctrl-C' to exit...", LogLevel.Info);

            var startTime = DateTime.Now;

            // Start capture 'INFINTE' number of packets
            // This method will return when EOF reached.
            device.Capture();

            // Close the pcap device
            device.Close();
            var endTime = DateTime.Now;
            WintapLogger.Log.Append("-- End of file reached.", LogLevel.Info);

            var duration = endTime - startTime;
            WintapLogger.Log.Append("Read {0} packets in {1}s", LogLevel.Info);

            Console.Write("Hit 'Enter' to exit...");
        }

        private void device_OnPacketArrival(object sender, PacketCapture e)
        {
            packetIndex++;

            var rawPacket = e.GetPacket();
            var packet = PacketDotNet.Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);

            var ethernetPacket = packet.Extract<EthernetPacket>();
            if (ethernetPacket != null)
            {
                OnEmit(new PcapEventArgs(packetIndex));
            }

        }

        protected virtual void OnEmit(PcapEventArgs e)
        {
            // Raise the event by using the delegate.
            Emit?.Invoke(this, e);
        }
    }
}
