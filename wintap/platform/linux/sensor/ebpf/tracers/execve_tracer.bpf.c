#include "vmlinux.h"
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>
#include <bpf/bpf_core_read.h>

// Maximum sizes for our data
#define TASK_COMM_LEN 16
#define MAX_FILENAME_LEN 256

// Minimal event structure - just the essentials
struct execve_event {
    // Process identification
    __u32 pid;
    __u32 ppid;              // Will be 0, filled in userspace
    __u32 uid;
    __u32 gid;
    __u32 sid;               // Simplified, filled in userspace
    
    // Process details
    char comm[TASK_COMM_LEN];
    char filename[MAX_FILENAME_LEN];
    
    // Timing
    __u64 timestamp_ns;
    
    // Placeholder fields (for struct compatibility)
    __u32 exit_code;
    __u32 flags;
    __u64 start_time;
    __u64 capabilities;
    __u32 seccomp_mode;
    
    // Note: Args, env vars, cwd all handled in userspace via /proc
};

// Ring buffer map
struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 256 * 1024);
} events SEC(".maps");

// Tracepoint structure for execve
struct trace_event_raw_sys_enter_execve {
    unsigned long long unused;
    long syscall_nr;
    const char *filename;
    const char *const *argv;
    const char *const *envp;
};

// Minimal tracepoint - just capture basics
SEC("tracepoint/syscalls/sys_enter_execve")
int trace_execve_entry(struct trace_event_raw_sys_enter_execve *ctx)
{
    struct execve_event *event;
    
    // Reserve space
    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return 0;
    
    // Get PID
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    event->pid = pid_tgid >> 32;
    
    // Get UID/GID
    __u64 uid_gid = bpf_get_current_uid_gid();
    event->uid = uid_gid & 0xFFFFFFFF;
    event->gid = uid_gid >> 32;
    
    // Get command name
    bpf_get_current_comm(&event->comm, sizeof(event->comm));
    
    // Get executable path
    if (ctx->filename) {
        bpf_probe_read_user_str(event->filename, sizeof(event->filename), ctx->filename);
    } else {
        event->filename[0] = '\0';
    }
    
    // Get timestamp
    event->timestamp_ns = bpf_ktime_get_ns();
    
    // Fields filled in userspace
    struct task_struct *task = (struct task_struct *)bpf_get_current_task_btf();
    struct task_struct *parent = BPF_CORE_READ(task, real_parent);
    event->ppid = parent ? BPF_CORE_READ(parent, tgid) : 0;
    event->sid = 0;
    event->exit_code = 0;
    event->flags = 0;
    event->start_time = 0;
    event->capabilities = 0;
    event->seccomp_mode = 0;
    
    // Submit
    bpf_ringbuf_submit(event, 0);
    
    return 0;
}

char LICENSE[] SEC("license") = "GPL";
