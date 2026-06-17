#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

#define TASK_COMM_LEN 16

struct exit_event {
    __u32 pid;
    __u32 ppid;
    __u32 uid;
    __u32 gid;
    char comm[TASK_COMM_LEN];
    __u64 timestamp_ns;
    __s32 exit_code;
};

struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 256 * 1024);
} events SEC(".maps");

struct sched_exit_args {
    unsigned long long unused;
    char comm[16];
    __s32 pid;
    int prio;
};

SEC("tracepoint/sched/sched_process_exit")
int trace_process_exit(struct sched_exit_args *ctx)
{
    struct exit_event *event;
    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return 0;

    event->pid = ctx->pid;
    event->ppid = 0;

    __u64 uid_gid = bpf_get_current_uid_gid();
    event->uid = uid_gid & 0xffffffff;
    event->gid = uid_gid >> 32;
    __builtin_memcpy(event->comm, ctx->comm, TASK_COMM_LEN);
    event->timestamp_ns = bpf_ktime_get_ns();
    event->exit_code = 0;

    bpf_ringbuf_submit(event, 0);
    return 0;
}

char LICENSE[] SEC("license") = "GPL";
