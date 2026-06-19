using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace gov.llnl.wintap.platform.linux.collect
{
    /// <summary>
    /// P/Invoke bindings for libbpf library
    /// Shared across all eBPF sensors
    /// </summary>
    public static class LibBpf
    {
        private const string LibBpfLib = "libbpf.so.1";

        static LibBpf()
        {
            // RHEL 8 commonly ships libbpf with SONAME libbpf.so.0, while other
            // distros may ship libbpf.so.1. Try both at runtime.
            NativeLibrary.SetDllImportResolver(typeof(LibBpf).Assembly, ResolveLibbpf);
        }

        private static IntPtr ResolveLibbpf(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, LibBpfLib, StringComparison.Ordinal))
            {
                return IntPtr.Zero;
            }

            // Keep the original first, then fall back for older distros.
            string[] candidates =
            {
                "libbpf.so.1",
                "libbpf.so.0",
                "libbpf.so",
            };

            foreach (string candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        // BPF object management
        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr bpf_object__open(string path);

        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int bpf_object__load(IntPtr obj);

        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void bpf_object__close(IntPtr obj);

        // Program management
        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr bpf_object__find_program_by_name(IntPtr obj, string name);

        // Iterate programs in an object. Pass IntPtr.Zero to get the first program.
        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr bpf_object__next_program(IntPtr obj, IntPtr prev);

        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr bpf_program__name(IntPtr prog);

        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr bpf_program__attach(IntPtr prog);

        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int bpf_link__destroy(IntPtr link);

        // Map management
        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr bpf_object__find_map_by_name(IntPtr obj, string name);

        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int bpf_map__fd(IntPtr map);

        // libbpf provides a thin wrapper around the bpf() syscall helpers.
        // We use this to set simple configuration maps (e.g. PID filters).
        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int bpf_map_update_elem(int fd, ref uint key, ref uint value, ulong flags);

        // Ring buffer
        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ring_buffer__new(int map_fd, RingBufferCallback cb, IntPtr ctx, IntPtr opts);

        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ring_buffer__free(IntPtr rb);

        [DllImport(LibBpfLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ring_buffer__poll(IntPtr rb, int timeout_ms);

        // Callback delegate
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int RingBufferCallback(IntPtr ctx, IntPtr data, UIntPtr size);
    }
}
