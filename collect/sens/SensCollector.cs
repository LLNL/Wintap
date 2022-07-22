/*
 * Copyright (c) 2021, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using COMAdmin;
using gov.llnl.wintap.collect.shared;
using System;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.collect.models;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using gov.llnl.wintap.core.shared;

namespace gov.llnl.wintap.collect
{
    /// <summary>
    /// System Event Notification Services (SENS) collector
    /// SENS:  https://docs.microsoft.com/en-us/windows/win32/sens/notifications
    /// </summary>
    internal class SensCollector : BaseCollector, SensEvents.ISensLogon
    {
        public SensCollector() : base()
        {
            this.CollectorName = "SensCollector";
            COMAdminCatalogClass comAdmin = new COMAdminCatalogClass();
            try
            {
                ICatalogCollection subCollection = (ICatalogCollection)comAdmin.GetCollection("TransientSubscriptions");
                SubscribeToEvent(subCollection, "DisplayUnlock", "{D5978630-5B9F-11D1-8DD2-00AA004ABD5E}");
                SubscribeToEvent(subCollection, "DisplayLock", "{D5978630-5B9F-11D1-8DD2-00AA004ABD5E}");
                SubscribeToEvent(subCollection, "Logon", "{D5978630-5B9F-11D1-8DD2-00AA004ABD5E}");
                SubscribeToEvent(subCollection, "Logoff", "{D5978630-5B9F-11D1-8DD2-00AA004ABD5E}");
                SubscribeToEvent(subCollection, "StartScreenSaver", "{D5978630-5B9F-11D1-8DD2-00AA004ABD5E}");
                SubscribeToEvent(subCollection, "StopScreenSaver", "{D5978630-5B9F-11D1-8DD2-00AA004ABD5E}");
                SubscribeToEvent(subCollection, "StartShell", "{D5978630-5B9F-11D1-8DD2-00AA004ABD5E}");
            }
            catch (Exception ex)
            {
                WintapLogger.Log.Append("error registering SENS events: " + ex.Message, LogLevel.Always);
            }

            WintapLogger.Log.Append("Releasing COM object for SENS", LogLevel.Always);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(comAdmin);
        }

        public void DisplayLock(string userName)
        {
            sendSessionChangeEvent("ScreenLock", userName);
        }
        public void DisplayUnlock(string userName)
        {
            sendSessionChangeEvent("ScreenUnlock", userName);
        }
        public void StartScreenSaver(string userName)
        {
            sendSessionChangeEvent("ScreenSaverStart", userName);
        }
        public void StopScreenSaver(string userName)
        {
            sendSessionChangeEvent("ScreenSaverStop", userName);
        }
        public void StartShell(string userName)
        {
            sendSessionChangeEvent("ShellStart", userName);
        }

        public void Logon(string userName)
        {
            sendSessionChangeEvent("Logon", userName);
        }
        public void Logoff(string userName)
        {
            sendSessionChangeEvent("LogOff", userName);
        }

        private void SubscribeToEvent(ICatalogCollection subCollection, string methodName, string guidString)
        {
            ICatalogObject catalogObject = (ICatalogObject)subCollection.Add();
            catalogObject.set_Value("EventCLSID", guidString);
            catalogObject.set_Value("Name", "Subscription to " + methodName + " event");
            catalogObject.set_Value("MethodName", methodName);
            catalogObject.set_Value("SubscriberInterface", this);
            catalogObject.set_Value("Enabled", true);
            subCollection.SaveChanges();
        }
        private void sendSessionChangeEvent(string description, string userName)
        {
            this.Counter++;
            WintapMessage msg = new WintapMessage(DateTime.Now, 4, "SessionChange");
            msg.ReceiveTime = DateTime.Now.ToFileTimeUtc();
            msg.SessionChange = new WintapMessage.SessionChangeObject() { UserName = userName, Description = description };
            msg.Send();
            WintapLogger.Log.Append("    User: " + userName + "  description: " + description, LogLevel.Always);
        }

    }
}
