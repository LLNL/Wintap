#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

// Maximum sizes for our data
#define TASK_COMM_LEN 16
#define MAX_FILENAME_LEN 256

// Event structure to send to userspace
struct openat_event {
    __u32 pid;
    char comm[TASK_COMM_LEN];
    char filename[MAX_FILENAME_LEN];
};

// Ring buffer map for sending events to userspace
struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 256 * 1024); // 256KB buffer
} events SEC(".maps");

// Architecture-agnostic tracepoint structure
// This works on both x86_64 and ARM64
struct syscalls_enter_openat_args {
    unsigned long long unused;
    long syscall_nr;
    long dfd;
    const char *filename;
    long flags;
    long mode;
};

// Tracepoint for openat syscall entry
// Works on both x86_64 and ARM64
SEC("tracepoint/syscalls/sys_enter_openat")
int trace_openat_entry(struct syscalls_enter_openat_args *ctx)
{
    struct openat_event *event;
    
    // Reserve space in the ring buffer
    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return 0;
    
    // Get PID and command name
    event->pid = bpf_get_current_pid_tgid() >> 32;
    bpf_get_current_comm(&event->comm, sizeof(event->comm));
    
    // Get filename from syscall arguments
    // ctx->filename is already the pointer to the filename
    bpf_probe_read_user_str(&event->filename, sizeof(event->filename), ctx->filename);
    
    // Submit the event
    bpf_ringbuf_submit(event, 0);
    
    return 0;
}

char LICENSE[] SEC("license") = "GPL";