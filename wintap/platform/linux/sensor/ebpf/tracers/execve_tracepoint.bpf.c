#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

#define TASK_COMM_LEN 16
#define MAX_FILENAME_LEN 256

#define EXEC_EVT_FLAG_SCHED_EXEC (1u << 31)

struct execve_event {
    __u32 pid;
    __u32 ppid;
    __u32 uid;
    __u32 gid;
    __u32 sid;
    char comm[TASK_COMM_LEN];
    char parent_comm[TASK_COMM_LEN];
    __u64 parent_start_ns;
    char filename[MAX_FILENAME_LEN];
    __u64 timestamp_ns;
    __u32 exit_code;
    __u32 flags;
    __u64 start_time;
    __u64 capabilities;
    __u32 seccomp_mode;
};

struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 256 * 1024);
} events SEC(".maps");

struct trace_event_raw_sys_enter_execve {
    unsigned long long unused;
    long syscall_nr;
    const char *filename;
    const char *const *argv;
    const char *const *envp;
};

struct trace_event_raw_sys_enter_execveat {
    unsigned long long unused;
    long syscall_nr;
    int dfd;
    const char *filename;
    const char *const *argv;
    const char *const *envp;
    int flags;
};

struct sched_process_exec_args {
    unsigned long long unused;
    __u32 filename; // __data_loc
    __s32 pid;
    __s32 old_pid;
};

SEC("tracepoint/syscalls/sys_enter_execve")
int trace_execve_entry(struct trace_event_raw_sys_enter_execve *ctx)
{
    struct execve_event *event;
    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return 0;

    __u64 pid_tgid = bpf_get_current_pid_tgid();
    event->pid = pid_tgid >> 32;
    event->ppid = 0;

    __u64 uid_gid = bpf_get_current_uid_gid();
    event->uid = uid_gid & 0xffffffff;
    event->gid = uid_gid >> 32;
    event->sid = 0;

    bpf_get_current_comm(&event->comm, sizeof(event->comm));
    __builtin_memset(event->parent_comm, 0, sizeof(event->parent_comm));
    event->parent_start_ns = 0;
    if (ctx->filename)
        bpf_probe_read_user_str(event->filename, sizeof(event->filename), ctx->filename);
    else
        event->filename[0] = '\0';

    event->timestamp_ns = bpf_ktime_get_ns();
    event->exit_code = 0;
    event->flags = 0;
    event->start_time = 0;
    event->capabilities = 0;
    event->seccomp_mode = 0;

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
    event->ppid = 0;

    __u64 uid_gid = bpf_get_current_uid_gid();
    event->uid = uid_gid & 0xffffffff;
    event->gid = uid_gid >> 32;
    event->sid = 0;

    bpf_get_current_comm(&event->comm, sizeof(event->comm));
    __builtin_memset(event->parent_comm, 0, sizeof(event->parent_comm));
    event->parent_start_ns = 0;
    if (ctx->filename)
        bpf_probe_read_user_str(event->filename, sizeof(event->filename), ctx->filename);
    else
        event->filename[0] = '\0';

    event->timestamp_ns = bpf_ktime_get_ns();
    event->exit_code = 0;
    event->flags = (__u32)ctx->flags;
    event->start_time = 0;
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

    event->pid = (__u32)ctx->pid;
    event->ppid = 0;

    __u64 uid_gid = bpf_get_current_uid_gid();
    event->uid = uid_gid & 0xffffffff;
    event->gid = uid_gid >> 32;
    event->sid = 0;

    bpf_get_current_comm(&event->comm, sizeof(event->comm));
    __builtin_memset(event->parent_comm, 0, sizeof(event->parent_comm));
    event->parent_start_ns = 0;

    __u32 loc = ctx->filename;
    __u32 off = loc & 0xFFFF;
    const char *fn = (const char *)ctx + off;
    bpf_probe_read_kernel_str(event->filename, sizeof(event->filename), fn);

    event->timestamp_ns = bpf_ktime_get_ns();
    event->exit_code = 0;
    event->flags = EXEC_EVT_FLAG_SCHED_EXEC;
    event->start_time = 0;
    event->capabilities = 0;
    event->seccomp_mode = 0;

    bpf_ringbuf_submit(event, 0);
    return 0;
}

char LICENSE[] SEC("license") = "GPL";
