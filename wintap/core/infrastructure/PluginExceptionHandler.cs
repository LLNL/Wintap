using System;
using System.Threading;
using gov.llnl.wintap.core.infrastructure;
using gov.llnl.wintap.collect.models;
using gov.llnl.wintap.core.shared;
using System.Threading.Tasks;
using System.Diagnostics;

namespace gov.llnl.wintap.core.infrastructure
{
    public class PluginExceptionHandler
    {
        private static readonly PluginExceptionHandler instance = new PluginExceptionHandler();

        public static PluginExceptionHandler Instance => instance;

        private PluginExceptionHandler()
        {
            // Set up global exception handlers
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
            TaskScheduler.UnobservedTaskException += HandleTaskException;
            Thread.GetDomain().UnhandledException += HandleUnhandledException;
        }

        public void Initialize()
        {
            // Called when Wintap starts to ensure handler is registered
            WintapLogger.Log.Append("Plugin Exception Handler initialized", LogLevel.Info);
        }

        private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            LogPluginException(exception, "Unhandled Exception");

            // Send alert through Wintap's event system
            SendWintapAlert(exception);

            // Don't terminate process if exception was not fatal
            if (!e.IsTerminating)
            {
                return;
            }

            // If terminating, attempt graceful shutdown
            try
            {
                WintapLogger.Log.Append($"Fatal unhandled exception in plugin. Attempting graceful shutdown: {exception?.Message}", LogLevel.Info);
                Utilities.RestartWintap($"Fatal plugin exception: {exception?.Message}");
            }
            catch (Exception shutdownEx)
            {
                WintapLogger.Log.Append($"Error during shutdown after fatal plugin exception: {shutdownEx.Message}", LogLevel.Info);
            }
        }

        private void HandleTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogPluginException(e.Exception, "Unobserved Task Exception");
            SendWintapAlert(e.Exception);
            e.SetObserved(); // Prevent process termination
        }

        private void LogPluginException(Exception ex, string type)
        {
            var pluginName = GetPluginNameFromStack(ex);
            var logMessage = $"Plugin Exception ({type}) in {pluginName}: {ex.Message}\nStack Trace: {ex.StackTrace}";

            if (ex.InnerException != null)
            {
                logMessage += $"\nInner Exception: {ex.InnerException.Message}\nInner Stack Trace: {ex.InnerException.StackTrace}";
            }

            WintapLogger.Log.Append(logMessage, LogLevel.Info);
        }

        private string GetPluginNameFromStack(Exception ex)
        {
            // Try to determine plugin name from stack trace
            try
            {
                var stack = ex.StackTrace;
                if (string.IsNullOrEmpty(stack))
                    return "Unknown Plugin";

                // Look for plugin namespace in stack trace
                var lines = stack.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("gov.llnl.wintap.plugins"))
                    {
                        var parts = line.Split('.');
                        // Return last namespace part before method name as plugin name
                        for (int i = parts.Length - 1; i >= 0; i--)
                        {
                            if (!parts[i].Contains("("))
                                return parts[i];
                        }
                    }
                }
            }
            catch
            {
                // Fallback if stack trace analysis fails
            }
            return "Unknown Plugin";
        }

        private void SendWintapAlert(Exception ex)
        {
            try
            {
                var pluginName = GetPluginNameFromStack(ex);
                var msg = new WintapMessage(DateTime.UtcNow, Process.GetCurrentProcess().Id, WintapMessage.MessageTypeEnum.WintapAlert);
                msg.WintapAlert = new WintapMessage.WintapAlertData
                {
                    AlertName = WintapMessage.WintapAlertData.AlertNameEnum.OTHER,
                    AlertDescription = $"Plugin Exception in {pluginName}: {ex.Message}"
                };
                EventChannel.Send(msg);
            }
            catch (Exception alertEx)
            {
                WintapLogger.Log.Append($"Failed to send alert for plugin exception: {alertEx.Message}", LogLevel.Info);
            }
        }

        public void WrapPluginMethod(Action method, string pluginName)
        {
            try
            {
                method();
            }
            catch (Exception ex)
            {
                LogPluginException(ex, $"Wrapped Exception in {pluginName}");
                SendWintapAlert(ex);
            }
        }

        public T WrapPluginMethod<T>(Func<T> method, string pluginName, T defaultValue = default)
        {
            try
            {
                return method();
            }
            catch (Exception ex)
            {
                LogPluginException(ex, $"Wrapped Exception in {pluginName}");
                SendWintapAlert(ex);
                return defaultValue;
            }
        }
    }
}