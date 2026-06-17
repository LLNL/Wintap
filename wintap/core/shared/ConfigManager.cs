using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Linq;

namespace gov.llnl.wintap.core.shared
{
    public static class ConfigManager
    {
        private static ConfigRoot _config;
        private const string DefaultConfigPath = "wintap/wintap/core/etl/ETLConfig.json";

        static ConfigManager()
        {
            LoadConfig();
        }

        private static void LoadConfig()
        {
            string configPath = Environment.GetEnvironmentVariable("WINTAP_CONFIG_PATH");
            if (string.IsNullOrEmpty(configPath))
            {
                // Try several sensible default locations relative to the running assembly and repository layout.
                var appBase = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(appBase, DefaultConfigPath),                // default: appBase/wintap/wintap/core/etl/ETLConfig.json
                    Path.Combine(appBase, "ETLConfig.json"),               // appBase/ETLConfig.json
                    Path.GetFullPath(Path.Combine(appBase, "..", "..", "..", "..", "wintap", "core", "etl", "ETLConfig.json")),
                    Path.GetFullPath(Path.Combine(appBase, "..", "..", "wintap", "core", "etl", "ETLConfig.json")),
                    Path.GetFullPath(Path.Combine(appBase, "..", "..", "..", "wintap", "core", "etl", "ETLConfig.json"))
                };

                configPath = candidates.FirstOrDefault(p => File.Exists(p));
            }

            if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    _config = JsonSerializer.Deserialize<ConfigRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    // Fallback to default if file is corrupted
                    _config = new ConfigRoot();
                }
            }
            else
            {
                _config = new ConfigRoot();
            }
        }

        // Attempts to resolve a configuration value by the provided key. The key may be either the
        // canonical config property name (e.g. "DataRoot") or an environment-style name
        // (e.g. "WINTAP_DATA_ROOT"). If no matching property exists, falls back to reading the
        // environment variable directly.
        public static T GetValue<T>(string key)
        {
            try
            {
                // Try direct property lookup (case-insensitive)
                var prop = typeof(ConfigRoot).GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null)
                {
                    // Try mapping env-style name to PascalCase (strip optional WINTAP_ prefix)
                    string mapped = MapEnvNameToProperty(key);
                    prop = typeof(ConfigRoot).GetProperty(mapped, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                }

                if (prop != null)
                {
                    var value = prop.GetValue(_config);
                    if (value == null) return default;
                    return (T)Convert.ChangeType(value, typeof(T));
                }

                // If no property matched, return default (do not read arbitrary OS environment variables).
                return default;
            }
            catch
            {
                return default;
            }
        }

        private static string MapEnvNameToProperty(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            string work = key;
            if (work.StartsWith("WINTAP_", StringComparison.OrdinalIgnoreCase))
            {
                work = work.Substring("WINTAP_".Length);
            }

            // Convert snake_case / UPPER_UNDERSCORE to PascalCase
            var parts = work.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var pascal = string.Join(string.Empty, parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
            return pascal;
        }
    }

    public class ConfigRoot
    {
        public string DataRoot { get; set; } = "/tmp/lintap-data";
        public bool DisableMCP { get; set; } = true;
        public bool DisableDuckDBUI { get; set; } = true;
        public bool DisableETL { get; set; } = false;
        public bool DisableSensors { get; set; } = false;
        public bool EarlyConsole { get; set; } = true;
        public bool DisableSettings { get; set; } = true;
        public bool EnableDirectParquet { get; set; } = false;
        public int DirectParquetFlushSeconds { get; set; } = 15;
        public bool Execve { get; set; } = true;
        public bool Clone { get; set; } = true;
        public bool Exit { get; set; } = true;
        public bool Network { get; set; } = true;
        public bool FileOps { get; set; } = true;
        public bool ProcessRundown { get; set; } = true;
        public bool SkipProcessResolve { get; set; } = false;
        public bool SkipParentProcessResolve { get; set; } = false;
        public bool SkipProcessRegister { get; set; } = false;
        public bool SkipEsperSend { get; set; } = false;
        public int ETLMaxQueueEvents { get; set; } = 10000;
        public int ETLMaxQueueEventsSerializer { get; set; } = 10000;
        public string ETLQueueDropPolicy { get; set; } = "newest";
        public int ParquetMaxBatchBacklog { get; set; } = 1000;
        public string ParquetBacklogDropPolicy { get; set; } = "newest";
        public int DirectParquetMaxQueueEvents { get; set; } = 5000;
        public string DirectParquetQueueDropPolicy { get; set; } = "newest";
        public int SerializationIntervalSec { get; set; } = 60;
        public int UploadIntervalSec { get; set; } = 300;
        public bool WriteToParquet { get; set; } = true;
        public string LogLevel { get; set; } = "Normal";
        public string SensorProfile { get; set; } = "Quality";
    }
}
