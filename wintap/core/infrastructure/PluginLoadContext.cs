using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace gov.llnl.wintap.core.infrastructure
{
    public class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginDirectory;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _pluginDirectory = Path.GetDirectoryName(pluginPath);
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // First, try to resolve using the plugin's dependency resolver
            string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            // Next, check if the assembly exists in the plugin's folder or lib subfolder
            string pluginAssemblyPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(pluginAssemblyPath))
            {
                return LoadFromAssemblyPath(pluginAssemblyPath);
            }

            string libDirectory = Path.Combine(_pluginDirectory, "lib");
            if (Directory.Exists(libDirectory))
            {
                string libAssemblyPath = Path.Combine(libDirectory, $"{assemblyName.Name}.dll");
                if (File.Exists(libAssemblyPath))
                {
                    return LoadFromAssemblyPath(libAssemblyPath);
                }
            }

            // If not found in plugin's dependencies, return null to allow the framework to resolve it
            return null;
        }
    }
}
