#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

#define TASK_COMM_LEN 16
#define MAX_FILENAME_LEN 256

enum file_op_type {
    FILE_OP_OPEN = 1,
    FILE_OP_READ = 2,
    FILE_OP_WRITE = 3,
    FILE_OP_CLOSE = 4,
    FILE_OP_MMAP = 5,
    FILE_OP_UNLINK = 6
};

struct file_event {
    __u32 pid;
    char comm[TASK_COMM_LEN];
    char filename[MAX_FILENAME_LEN];
    __u64 timestamp_ns;
    __u32 fd;
    __u32 bytes;
    __u32 op_type;
};

struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 512 * 1024);
} events SEC(".maps");

static __always_inline void emit_file_event(__u32 pid, const char *filename, __u32 fd, __u32 bytes, __u32 op_type)
{
    struct file_event *event;
    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return;

    event->pid = pid;
    bpf_get_current_comm(&event->comm, sizeof(event->comm));
    if (filename)
        bpf_probe_read_user_str(&event->filename, sizeof(event->filename), filename);
    else
        event->filename[0] = '\0';
    event->timestamp_ns = bpf_ktime_get_ns();
    event->fd = fd;
    event->bytes = bytes;
    event->op_type = op_type;
    bpf_ringbuf_submit(event, 0);
}

struct openat_args {
    unsigned long long unused;
    long syscall_nr;
    long dfd;
    const char *filename;
    long flags;
    long mode;
};

SEC("tracepoint/syscalls/sys_enter_openat")
int trace_openat(struct openat_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    emit_file_event(pid_tgid >> 32, ctx->filename, 0, 0, FILE_OP_OPEN);
    return 0;
}

struct read_enter_args {
    unsigned long long unused;
    long syscall_nr;
    unsigned int fd;
    char *buf;
    __u64 count;
};

SEC("tracepoint/syscalls/sys_enter_read")
int trace_read_enter(struct read_enter_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    emit_file_event(pid_tgid >> 32, 0, ctx->fd, 0, FILE_OP_READ);
    return 0;
}

struct write_enter_args {
    unsigned long long unused;
    long syscall_nr;
    unsigned int fd;
    const char *buf;
    __u64 count;
};

SEC("tracepoint/syscalls/sys_enter_write")
int trace_write_enter(struct write_enter_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    emit_file_event(pid_tgid >> 32, 0, ctx->fd, (__u32)ctx->count, FILE_OP_WRITE);
    return 0;
}

struct close_args {
    unsigned long long unused;
    long syscall_nr;
    unsigned int fd;
};

SEC("tracepoint/syscalls/sys_enter_close")
int trace_close(struct close_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    emit_file_event(pid_tgid >> 32, 0, ctx->fd, 0, FILE_OP_CLOSE);
    return 0;
}

struct mmap_args {
    unsigned long long unused;
    long syscall_nr;
    unsigned long addr;
    unsigned long len;
    unsigned long prot;
    unsigned long flags;
    unsigned long fd;
    unsigned long off;
};

SEC("tracepoint/syscalls/sys_enter_mmap")
int trace_mmap(struct mmap_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    if (ctx->fd != -1)
        emit_file_event(pid_tgid >> 32, 0, ctx->fd, ctx->len, FILE_OP_MMAP);
    return 0;
}

struct unlinkat_args {
    unsigned long long unused;
    long syscall_nr;
    int dfd;
    const char *pathname;
    int flag;
};

SEC("tracepoint/syscalls/sys_enter_unlinkat")
int trace_unlinkat(struct unlinkat_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    emit_file_event(pid_tgid >> 32, ctx->pathname, 0, 0, FILE_OP_UNLINK);
    return 0;
}

char LICENSE[] SEC("license") = "GPL";
