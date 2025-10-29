#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

#define TASK_COMM_LEN 16

// Event structure for process exit
struct exit_event {
    // Process identification
    __u32 pid;
    __u32 ppid;        // Will be 0, filled in userspace
    __u32 uid;
    __u32 gid;
    
    // Process details
    char comm[TASK_COMM_LEN];
    
    // Timing
    __u64 timestamp_ns;
    
    // Exit information
    __s32 exit_code;   // Exit status (if available)
};

// Ring buffer map
struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 256 * 1024);
} events SEC(".maps");

// Tracepoint structure for sched_process_exit
// This is a minimal safe structure
struct sched_exit_args {
    unsigned long long unused;
    char comm[16];
    __s32 pid;
    int prio;
};

// Hook process exit
SEC("tracepoint/sched/sched_process_exit")
int trace_process_exit(struct sched_exit_args *ctx)
{
    struct exit_event *event;
    
    // Reserve space
    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return 0;
    
    // Get PID (from tracepoint args is more reliable than current during exit)
    event->pid = ctx->pid;
    
    // Get UID/GID (still available during exit)
    __u64 uid_gid = bpf_get_current_uid_gid();
    event->uid = uid_gid & 0xFFFFFFFF;
    event->gid = uid_gid >> 32;
    
    // Get command name (from tracepoint)
    __builtin_memcpy(event->comm, ctx->comm, TASK_COMM_LEN);
    
    // Get timestamp
    event->timestamp_ns = bpf_ktime_get_ns();
    
    // Fields filled in userspace
    event->ppid = 0;
    event->exit_code = 0;  // Exit code is tricky to get reliably
    
    // Submit
    bpf_ringbuf_submit(event, 0);
    
    return 0;
}

char LICENSE[] SEC("license") = "GPL";