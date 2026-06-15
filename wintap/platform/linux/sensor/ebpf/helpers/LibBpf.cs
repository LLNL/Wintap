using System;
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
