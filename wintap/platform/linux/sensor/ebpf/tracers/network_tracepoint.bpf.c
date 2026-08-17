#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

#define TASK_COMM_LEN 16
#define AF_INET 2
#define PROTO_TCP 6
#define PROTO_UDP 17

#define NET_OP_TCP_CONNECT 1
#define NET_OP_TCP_ACCEPT 2
#define NET_OP_TCP_CLOSE 5
#define NET_OP_UDP_SEND 6
#define NET_OP_UDP_RECV 7

#define TCP_ESTABLISHED 1
#define TCP_CLOSE 7
#define TCP_LISTEN 10

#define bpf_ntohs(x) __builtin_bswap16(x)

struct network_event {
    __u32 pid;
    char comm[TASK_COMM_LEN];
    __u64 timestamp_ns;
    __u32 saddr;
    __u32 daddr;
    __u16 sport;
    __u16 dport;
    __u8 protocol;
    __u8 op_type;
    __u32 bytes;
    __u8 is_ipv6;
    __u8 saddr_v6[16];
    __u8 daddr_v6[16];
};

struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 512 * 1024);
} events SEC(".maps");

struct {
    __uint(type, BPF_MAP_TYPE_ARRAY);
    __type(key, __u32);
    __type(value, __u32);
    __uint(max_entries, 1);
} capture_pid SEC(".maps");

struct recvfrom_ctx {
    void *src_addr;
    int *addrlen;
};

struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __type(key, __u64);
    __type(value, struct recvfrom_ctx);
    __uint(max_entries, 4096);
} recvfrom_ctx_map SEC(".maps");

static __always_inline int pid_allowed(__u32 pid)
{
    __u32 key = 0;
    __u32 *want = bpf_map_lookup_elem(&capture_pid, &key);
    if (!want || *want == 0)
        return 1;
    return pid == *want;
}

static __always_inline void emit_network_event(__u32 pid, __u32 saddr, __u32 daddr,
                                               __u16 sport, __u16 dport, __u8 protocol,
                                               __u8 op_type, __u32 bytes)
{
    struct network_event *event;
    event = bpf_ringbuf_reserve(&events, sizeof(*event), 0);
    if (!event)
        return;

    event->pid = pid;
    bpf_get_current_comm(&event->comm, sizeof(event->comm));
    event->timestamp_ns = bpf_ktime_get_ns();
    event->saddr = saddr;
    event->daddr = daddr;
    event->sport = sport;
    event->dport = dport;
    event->protocol = protocol;
    event->op_type = op_type;
    event->bytes = bytes;
    event->is_ipv6 = 0;
    __builtin_memset(event->saddr_v6, 0, sizeof(event->saddr_v6));
    __builtin_memset(event->daddr_v6, 0, sizeof(event->daddr_v6));

    bpf_ringbuf_submit(event, 0);
}

struct inet_sock_set_state_args {
    unsigned long long unused;
    const void *skaddr;
    int oldstate;
    int newstate;
    __u16 sport;
    __u16 dport;
    __u16 family;
    __u16 protocol;
    __u8 saddr[4];
    __u8 daddr[4];
    __u8 saddr_v6[16];
    __u8 daddr_v6[16];
};

SEC("tracepoint/sock/inet_sock_set_state")
int trace_inet_sock_set_state(struct inet_sock_set_state_args *ctx)
{
    if (ctx->family != AF_INET || ctx->protocol != PROTO_TCP)
        return 0;

    __u64 pid_tgid = bpf_get_current_pid_tgid();
    __u32 pid = pid_tgid >> 32;
    if (!pid_allowed(pid))
        return 0;

    __u32 saddr = 0, daddr = 0;
    __builtin_memcpy(&saddr, ctx->saddr, sizeof(saddr));
    __builtin_memcpy(&daddr, ctx->daddr, sizeof(daddr));

    __u8 op = 0;
    if (ctx->newstate == TCP_ESTABLISHED)
        op = ctx->oldstate == TCP_LISTEN ? NET_OP_TCP_ACCEPT : NET_OP_TCP_CONNECT;
    else if (ctx->newstate == TCP_CLOSE)
        op = NET_OP_TCP_CLOSE;

    if (op != 0)
        emit_network_event(pid, saddr, daddr, ctx->sport, ctx->dport, PROTO_TCP, op, 0);

    return 0;
}

struct wintap_sockaddr_in {
    __u16 sin_family;
    __u16 sin_port;
    __u32 sin_addr;
};

struct sendto_args {
    unsigned long long unused;
    long syscall_nr;
    int fd;
    void *buf;
    __u64 len;
    unsigned int flags;
    void *dest_addr;
    int addrlen;
};

SEC("tracepoint/syscalls/sys_enter_sendto")
int trace_sendto(struct sendto_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    __u32 pid = pid_tgid >> 32;
    if (!pid_allowed(pid) || !ctx->dest_addr)
        return 0;

    struct wintap_sockaddr_in addr = {};
    bpf_probe_read_user(&addr, sizeof(addr), ctx->dest_addr);
    if (addr.sin_family == AF_INET)
        emit_network_event(pid, 0, addr.sin_addr, 0, bpf_ntohs(addr.sin_port), PROTO_UDP, NET_OP_UDP_SEND, (__u32)ctx->len);

    return 0;
}

struct recvfrom_args {
    unsigned long long unused;
    long syscall_nr;
    int fd;
    void *buf;
    __u64 len;
    unsigned int flags;
    void *src_addr;
    int *addrlen;
};

SEC("tracepoint/syscalls/sys_enter_recvfrom")
int trace_recvfrom(struct recvfrom_args *ctx)
{
    if (ctx->src_addr) {
        __u64 pid_tgid = bpf_get_current_pid_tgid();
        struct recvfrom_ctx rctx = {};
        rctx.src_addr = ctx->src_addr;
        rctx.addrlen = ctx->addrlen;
        bpf_map_update_elem(&recvfrom_ctx_map, &pid_tgid, &rctx, BPF_ANY);
    }
    return 0;
}

struct sys_exit_args {
    unsigned long long unused;
    long syscall_nr;
    long ret;
};

SEC("tracepoint/syscalls/sys_exit_recvfrom")
int trace_recvfrom_exit(struct sys_exit_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    struct recvfrom_ctx *rctx = bpf_map_lookup_elem(&recvfrom_ctx_map, &pid_tgid);
    if (!rctx)
        return 0;

    bpf_map_delete_elem(&recvfrom_ctx_map, &pid_tgid);
    if (ctx->ret <= 0)
        return 0;

    struct wintap_sockaddr_in addr = {};
    bpf_probe_read_user(&addr, sizeof(addr), rctx->src_addr);
    if (addr.sin_family != AF_INET)
        return 0;

    __u32 pid = pid_tgid >> 32;
    if (!pid_allowed(pid))
        return 0;

    emit_network_event(pid, addr.sin_addr, 0, bpf_ntohs(addr.sin_port), 0, PROTO_UDP, NET_OP_UDP_RECV, (__u32)ctx->ret);
    return 0;
}

char LICENSE[] SEC("license") = "GPL";
