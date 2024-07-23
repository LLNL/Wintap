/*
 * Copyright (c) 2022, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace gov.llnl.wintap.collect.shared.helpers
{
    abstract internal class BaseEvent
    {
        private int pid;
        /// <summary>
        /// OS defined ID for this process
        /// </summary>
        public int PID
        {
            get
            {
                return pid;
            }
            set
            {
                pid = value;
            }
        }

        private DateTime eventTime;
        /// <summary>
        /// Discovery begin event as defined by the event provider.
        /// </summary>
        public DateTime EventTime
        {
            get
            {
                return eventTime;
            }
            set
            {
                eventTime = value;
            }
        }

        private DateTime receiveTime;
        /// <summary>
        /// Date/time the event was first received by wintap
        /// </summary>
        public DateTime ReceiveTime
        {
            get
            {
                return receiveTime;
            }
            set
            {
                receiveTime = value;
            }
        }

        private TimeSpan eventDelay;
        /// <summary>
        /// Milliseconds between the time of a native event being received by wintap and the time contained within it.
        /// used to determine event subscription performance.
        /// </summary>
        public TimeSpan EventDelay
        {
            get
            {
                return eventDelay;
            }
            set
            {
                eventDelay = value;
            }
        }

        public string[] EventArray { get; set; }

        public BaseEvent(TraceEvent obj)
        {
            string dted = obj.ToString();
            this.EventArray = dted.Split(new char[] { '\"' });
            this.PID = Convert.ToInt32(this.EventArray[3]);
        }
    }

    internal class RegKeyEvent : BaseEvent
    {
        public RegKeyEvent(TraceEvent obj) : base(obj)
        {
            path = new StringBuilder();
            this.KeyObject = ulong.Parse(this.EventArray[15].Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);
            this.BaseObject = ulong.Parse(this.EventArray[13].Replace("0x", ""), System.Globalization.NumberStyles.HexNumber); 
            this.Path.Append(EventArray[23].ToLower().Trim(new char[] { '\\' }));
            this.EventTime = obj.TimeStamp;
            this.ReceiveTime = DateTime.Now;
        }
        
        private ulong keyObject;
        public ulong KeyObject
        {
            get { return keyObject; }
            set { keyObject = value; }
        }

        private ulong baseObject;
        public ulong BaseObject
        {
            get { return baseObject; }
            set { baseObject = value; }
        }

        private StringBuilder path;
        public StringBuilder Path
        {
            get { return path; }
            set { path = value; }
        }

        internal void FixPath(TraceEvent obj, Dictionary<ulong, string> regParents)
        {
            this.Path.Clear();
            this.Path.Append(regParents[this.BaseObject]);
            this.Path.Append(@"\");
            this.Path.Append(this.EventArray[23].ToLower().Trim(new char[] { '\\' }));
        }
    }

    internal class RegDeleteValueEvent : BaseEvent
    {
        internal ulong BaseObject { get; set; }

        internal string ValueName { get; set; }

        internal RegDeleteValueEvent(TraceEvent obj) : base(obj)
        {
            this.BaseObject = ulong.Parse(this.EventArray[13].Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);
            this.ValueName = this.EventArray[19];
        }
    }

    internal class RegDeleteKeyEvent : BaseEvent
    {
        internal ulong BaseObject { get; set; }

        internal RegDeleteKeyEvent(TraceEvent obj) : base(obj)
        {
            this.BaseObject = ulong.Parse(this.EventArray[13].Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);
        }
    }

    class RegCloseEvent : BaseEvent
    {
        internal RegCloseEvent(TraceEvent obj) : base(obj)
        {
            this.BaseObject = ulong.Parse(this.EventArray[13].Replace("0x", ""), System.Globalization.NumberStyles.HexNumber);
        }

        internal ulong BaseObject { get; set; }
    }

    class RegSetValueEvent : BaseEvent
    {
        internal RegSetValueEvent(TraceEvent obj) : base(obj)
        {
            this.Handle = (ulong)obj.PayloadByName("KeyObject");
            this.Name = (string)obj.PayloadByName("ValueName");
            string keyname = (string)obj.PayloadByName("KeyName");
            this.DataType = (int)obj.PayloadByName("Type");
        }

        private ulong handle;
        public ulong Handle
        {
            get { return handle; }
            set { handle = value; }
        }

        private string name;
        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public int DataType { get; set; }
    }
}
