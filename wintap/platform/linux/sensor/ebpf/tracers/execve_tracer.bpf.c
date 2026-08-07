#include "vmlinux.h"
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>
#include <bpf/bpf_core_read.h>

// Maximum sizes for our data
#define TASK_COMM_LEN 16
#define MAX_FILENAME_LEN 256

// Breadcrumb flag: event emitted from sched:sched_process_exec (post-exec)
#define EXEC_EVT_FLAG_SCHED_EXEC (1u << 31)

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
    // Best-effort parent identity captured from task_struct (does not require /proc).
    // real_start_time is in the boottime clock domain, matching /proc's starttime basis.
    char parent_comm[TASK_COMM_LEN];
    __u64 parent_start_ns;
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

// Tracepoint structure for execveat
struct trace_event_raw_sys_enter_execveat {
    unsigned long long unused;
    long syscall_nr;
    int dfd;
    const char *filename;
    const char *const *argv;
    const char *const *envp;
    int flags;
};

// sched:sched_process_exec
struct sched_process_exec_args {
    unsigned long long unused;
    __u32 filename; // __data_loc
    __s32 pid;
    __s32 old_pid;
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
    event->start_time = BPF_CORE_READ(task, start_time);
    struct task_struct *parent = BPF_CORE_READ(task, real_parent);
    event->ppid = parent ? BPF_CORE_READ(parent, tgid) : 0;

    // Capture parent comm + start_time for stable parent hashing without /proc.
    if (parent) {
        bpf_core_read(event->parent_comm, sizeof(event->parent_comm), &parent->comm);
        event->parent_start_ns = BPF_CORE_READ(parent, start_time);
    } else {
        __builtin_memset(event->parent_comm, 0, sizeof(event->parent_comm));
        event->parent_start_ns = 0;
    }
    event->sid = 0;
    event->exit_code = 0;
    event->flags = 0;
    event->capabilities = 0;
    event->seccomp_mode = 0;
    
    // Submit
    bpf_ringbuf_submit(event, 0);
    
    return 0;
}

SEC("tracepoint/syscalls/sys_enter_execveat")
int trace_execveat_entry(struct trace_event_raw_sys_enter_execveat *ctx)
{
    struct execve_event *event;

    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return 0;

    __u64 pid_tgid = bpf_get_current_pid_tgid();
    event->pid = pid_tgid >> 32;

    __u64 uid_gid = bpf_get_current_uid_gid();
    event->uid = uid_gid & 0xFFFFFFFF;
    event->gid = uid_gid >> 32;

    bpf_get_current_comm(&event->comm, sizeof(event->comm));

    if (ctx->filename) {
        bpf_probe_read_user_str(event->filename, sizeof(event->filename), ctx->filename);
    } else {
        event->filename[0] = '\0';
    }

    event->timestamp_ns = bpf_ktime_get_ns();

    // Best-effort parent pid from task_struct.
    struct task_struct *task = (struct task_struct *)bpf_get_current_task_btf();
    struct task_struct *parent = BPF_CORE_READ(task, real_parent);
    event->ppid = parent ? BPF_CORE_READ(parent, tgid) : 0;

    if (parent) {
        bpf_core_read(event->parent_comm, sizeof(event->parent_comm), &parent->comm);
        event->parent_start_ns = BPF_CORE_READ(parent, start_time);
    } else {
        __builtin_memset(event->parent_comm, 0, sizeof(event->parent_comm));
        event->parent_start_ns = 0;
    }

    // Preserve the execveat flags (helps explain odd path forms in post-processing).
    event->flags = (__u32)ctx->flags;

    event->start_time = BPF_CORE_READ(task, start_time);

    // Remaining fields are filled in userspace
    event->sid = 0;
    event->exit_code = 0;
    event->capabilities = 0;
    event->seccomp_mode = 0;

    bpf_ringbuf_submit(event, 0);
    return 0;
}

SEC("tracepoint/sched/sched_process_exec")
int trace_sched_process_exec(struct sched_process_exec_args *ctx)
{
    struct execve_event *event;

    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return 0;

    // PID from tracepoint is reliable.
    event->pid = (__u32)ctx->pid;

    __u64 uid_gid = bpf_get_current_uid_gid();
    event->uid = uid_gid & 0xFFFFFFFF;
    event->gid = uid_gid >> 32;

    bpf_get_current_comm(&event->comm, sizeof(event->comm));

    // __data_loc encoding: lower 16 bits is offset from start of record.
    __u32 loc = ctx->filename;
    __u32 off = loc & 0xFFFF;
    const char *fn = (const char *)ctx + off;
    bpf_probe_read_kernel_str(event->filename, sizeof(event->filename), fn);

    event->timestamp_ns = bpf_ktime_get_ns();

    struct task_struct *task = (struct task_struct *)bpf_get_current_task_btf();
    struct task_struct *parent = BPF_CORE_READ(task, real_parent);
    event->ppid = parent ? BPF_CORE_READ(parent, tgid) : 0;

    if (parent) {
        bpf_core_read(event->parent_comm, sizeof(event->parent_comm), &parent->comm);
        event->parent_start_ns = BPF_CORE_READ(parent, start_time);
    } else {
        __builtin_memset(event->parent_comm, 0, sizeof(event->parent_comm));
        event->parent_start_ns = 0;
    }

    event->sid = 0;
    event->exit_code = 0;
    event->flags = EXEC_EVT_FLAG_SCHED_EXEC;
    event->start_time = BPF_CORE_READ(task, start_time);
    event->capabilities = 0;
    event->seccomp_mode = 0;

    bpf_ringbuf_submit(event, 0);
    return 0;
}

char LICENSE[] SEC("license") = "GPL";
