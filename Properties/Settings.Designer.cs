﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace gov.llnl.wintap.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.4.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Normal")]
        public string LoggingLevel {
            get {
                return ((string)(this["LoggingLevel"]));
            }
            set {
                this["LoggingLevel"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("8099")]
        public int ApiPort {
            get {
                return ((int)(this["ApiPort"]));
            }
            set {
                this["ApiPort"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Overwrite")]
        public string LoggingMode {
            get {
                return ((string)(this["LoggingMode"]));
            }
            set {
                this["LoggingMode"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool EnableWorkbench {
            get {
                return ((bool)(this["EnableWorkbench"]));
            }
            set {
                this["EnableWorkbench"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool ProcessCollector {
            get {
                return ((bool)(this["ProcessCollector"]));
            }
            set {
                this["ProcessCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool FileCollector {
            get {
                return ((bool)(this["FileCollector"]));
            }
            set {
                this["FileCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool TcpCollector {
            get {
                return ((bool)(this["TcpCollector"]));
            }
            set {
                this["TcpCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool UdpCollector {
            get {
                return ((bool)(this["UdpCollector"]));
            }
            set {
                this["UdpCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MicrosoftWindowsKernelRegistryCollector {
            get {
                return ((bool)(this["MicrosoftWindowsKernelRegistryCollector"]));
            }
            set {
                this["MicrosoftWindowsKernelRegistryCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool SensCollector {
            get {
                return ((bool)(this["SensCollector"]));
            }
            set {
                this["SensCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool ImageLoadCollector {
            get {
                return ((bool)(this["ImageLoadCollector"]));
            }
            set {
                this["ImageLoadCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MicrosoftWindowsKernelProcessCollector {
            get {
                return ((bool)(this["MicrosoftWindowsKernelProcessCollector"]));
            }
            set {
                this["MicrosoftWindowsKernelProcessCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MicrosoftWindowsWin32kCollector {
            get {
                return ((bool)(this["MicrosoftWindowsWin32kCollector"]));
            }
            set {
                this["MicrosoftWindowsWin32kCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MicrosoftWindowsWMIActivityCollector {
            get {
                return ((bool)(this["MicrosoftWindowsWMIActivityCollector"]));
            }
            set {
                this["MicrosoftWindowsWMIActivityCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool WindowsEventlogCollector {
            get {
                return ((bool)(this["WindowsEventlogCollector"]));
            }
            set {
                this["WindowsEventlogCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool MicrosoftWindowsCpuTriggerCollector {
            get {
                return ((bool)(this["MicrosoftWindowsCpuTriggerCollector"]));
            }
            set {
                this["MicrosoftWindowsCpuTriggerCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MicrosoftWindowsKernelMemoryCollector {
            get {
                return ((bool)(this["MicrosoftWindowsKernelMemoryCollector"]));
            }
            set {
                this["MicrosoftWindowsKernelMemoryCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool WebActivityCollector {
            get {
                return ((bool)(this["WebActivityCollector"]));
            }
            set {
                this["WebActivityCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<ArrayOfString xmlns:xsd=\"http://www.w3." +
            "org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" />")]
        public global::System.Collections.Specialized.StringCollection GenericProviders {
            get {
                return ((global::System.Collections.Specialized.StringCollection)(this["GenericProviders"]));
            }
            set {
                this["GenericProviders"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<ArrayOfString xmlns:xsd=\"http://www.w3." +
            "org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" />")]
        public global::System.Collections.Specialized.StringCollection ExemptPlugins {
            get {
                return ((global::System.Collections.Specialized.StringCollection)(this["ExemptPlugins"]));
            }
            set {
                this["ExemptPlugins"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MicrosoftWindowsGroupPolicyCollector {
            get {
                return ((bool)(this["MicrosoftWindowsGroupPolicyCollector"]));
            }
            set {
                this["MicrosoftWindowsGroupPolicyCollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool MicrosoftWindowsBitLockerAPICollector {
            get {
                return ((bool)(this["MicrosoftWindowsBitLockerAPICollector"]));
            }
            set {
                this["MicrosoftWindowsBitLockerAPICollector"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool CollectFileRead {
            get {
                return ((bool)(this["CollectFileRead"]));
            }
            set {
                this["CollectFileRead"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool CollectRegistryRead {
            get {
                return ((bool)(this["CollectRegistryRead"]));
            }
            set {
                this["CollectRegistryRead"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Developer")]
        public string Profile {
            get {
                return ((string)(this["Profile"]));
            }
            set {
                this["Profile"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1.0.0.0")]
        public string ConfigVersion {
            get {
                return ((string)(this["ConfigVersion"]));
            }
            set {
                this["ConfigVersion"] = value;
            }
        }
    }
}
