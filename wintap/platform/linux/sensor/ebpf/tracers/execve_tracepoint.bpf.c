#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

#define TASK_COMM_LEN 16
#define MAX_FILENAME_LEN 256

struct execve_event {
    __u32 pid;
    __u32 ppid;
    __u32 uid;
    __u32 gid;
    __u32 sid;
    char comm[TASK_COMM_LEN];
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

char LICENSE[] SEC("license") = "GPL";
