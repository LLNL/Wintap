#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

#define TASK_COMM_LEN 16

// Event structure for clone/fork/vfork
struct clone_event {
    // Parent process (the one calling fork/clone)
    __u32 parent_pid;
    __u32 parent_uid;
    __u32 parent_gid;
    char parent_comm[TASK_COMM_LEN];
    
    // Child process (newly created)
    __u32 child_pid;
    
    // Timing
    __u64 timestamp_ns;
    
    // Clone flags (indicates fork vs vfork vs clone)
    __u64 clone_flags;
};

// Ring buffer map
struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 256 * 1024);
} events SEC(".maps");

// Tracepoint structure for sched_process_fork
struct sched_fork_args {
    unsigned long long unused;
    char parent_comm[16];
    __s32 parent_pid;
    char child_comm[16];
    __s32 child_pid;
};

// Hook process fork/clone/vfork
// This tracepoint fires for fork(), vfork(), and clone()
SEC("tracepoint/sched/sched_process_fork")
int trace_process_fork(struct sched_fork_args *ctx)
{
    struct clone_event *event;
    
    // Reserve space
    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return 0;
    
    // Get parent process info
    event->parent_pid = ctx->parent_pid;
    
    // Get parent UID/GID
    __u64 uid_gid = bpf_get_current_uid_gid();
    event->parent_uid = uid_gid & 0xFFFFFFFF;
    event->parent_gid = uid_gid >> 32;
    
    // Get parent command name (from tracepoint)
    __builtin_memcpy(event->parent_comm, ctx->parent_comm, TASK_COMM_LEN);
    
    // Get child PID (from tracepoint)
    event->child_pid = ctx->child_pid;
    
    // Get timestamp
    event->timestamp_ns = bpf_ktime_get_ns();
    
    // Clone flags not available from this tracepoint
    // Would need sys_enter_clone for that
    event->clone_flags = 0;
    
    // Submit
    bpf_ringbuf_submit(event, 0);
    
    return 0;
}

char LICENSE[] SEC("license") = "GPL";