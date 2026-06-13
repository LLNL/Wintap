#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>
// Ensure user_pt_regs is visible for PT_REGS_* macros on some platforms
#include <linux/ptrace.h>

// Network byte order conversion
#define bpf_ntohs(x) __builtin_bswap16(x)

#define TASK_COMM_LEN 16

// TCP state values from include/net/tcp_states.h
#define TCP_ESTABLISHED 1
#define TCP_SYN_SENT 2
#define TCP_SYN_RECV 3
#define TCP_CLOSE 7
#define TCP_LISTEN 10

#define AF_INET 2

// Network operation types
enum net_op_type {
    NET_OP_TCP_CONNECT = 1,
    NET_OP_TCP_ACCEPT = 2,
    NET_OP_TCP_SEND = 3,
    NET_OP_TCP_RECV = 4,
    NET_OP_TCP_CLOSE = 5,
    NET_OP_UDP_SEND = 6,
    NET_OP_UDP_RECV = 7
};

// Network protocol types
enum net_proto {
    PROTO_TCP = 6,
    PROTO_UDP = 17
};

// Event structure for network operations
struct network_event {
    __u32 pid;
    char comm[TASK_COMM_LEN];
    __u64 timestamp_ns;
    
    // Connection info
    __u32 saddr;
    __u32 daddr;
    __u16 sport;
    __u16 dport;
    __u8 protocol;
    __u8 op_type;
    
    // Data size
    __u32 bytes;
    
    // IPv6 support (for future)
    __u8 is_ipv6;
    __u8 saddr_v6[16];
    __u8 daddr_v6[16];
};

// Ring buffer map
struct {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, 512 * 1024);
} events SEC(".maps");

// Diagnostic counters: per-CPU array with indexes:
// 0 = STORE, 1 = HIT (lookup found), 2 = MISS (lookup absent)
// Use a per-cpu array so increments are cheaper and avoid cross-cpu races.
struct {
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __type(key, __u32);
    __type(value, __u64);
    __uint(max_entries, 4);
} diag_counters SEC(".maps");

static __always_inline void diag_inc(__u32 idx)
{
    __u64 *val = bpf_map_lookup_elem(&diag_counters, &idx);
    if (val) {
        __sync_fetch_and_add(val, 1);
    }
}

// Map to store PID (and timestamp) keyed by socket pointer. We populate this
// at syscall/kprobe time (user/process context) and consult it in the
// inet_sock_set_state tracepoint which often runs in softirq where
// bpf_get_current_pid_tgid() is unreliable for process attribution.
struct sock_info {
    __u32 pid;
    __u64 start_ns;
};

// Use a 64-bit integer key for socket pointer to avoid ABI/typing issues when
// passing pointers between kprobe and tracepoint contexts. Using __u64 is
// more portable than `void *` as a map key across libbpf/clang toolchains.
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __type(key, __u64);
    __type(value, struct sock_info);
    __uint(max_entries, 10240);
} sock_pid_map SEC(".maps");

// Helper to emit network event
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
    
    bpf_ringbuf_submit(event, 0);
}

// Emit a small in-band diagnostic event via the same ring buffer. We use
// protocol=0xFF to indicate diagnostics and put a small code in op_type.
// The 'bytes' field holds the low 32 bits of the socket pointer to help
// correlate events; sport holds low 16 bits as well for quick human-read.
static __always_inline void emit_diag_event(__u8 diag_code, __u32 pid, void *sk)
{
    __u32 sk_lo = 0;
    __u16 sk_lo16 = 0;
    if (sk) {
        __u64 sk64 = (__u64)sk;
        sk_lo = (__u32)(sk64 & 0xffffffffULL);
        sk_lo16 = (__u16)(sk64 & 0xffffULL);
    }

    // Use emit_network_event with protocol=0xFF to signal a diagnostic
    emit_network_event(pid, 0, 0, 0, sk_lo16, 0xFF, diag_code, sk_lo);
}

// sockaddr_in structure (IPv4)
struct sockaddr_in {
    __u16 sin_family;
    __u16 sin_port;
    __u32 sin_addr;
};

// sock:inet_sock_set_state tracepoint context. This tracepoint fires after
// the kernel has assigned socket addresses, so it includes the local address
// that is not available at sys_enter_connect time.
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

    __u8 op_type = 0;

    if (ctx->newstate == TCP_ESTABLISHED)
    {
        if (ctx->oldstate == TCP_SYN_SENT)
            op_type = NET_OP_TCP_CONNECT;
        else if (ctx->oldstate == TCP_SYN_RECV || ctx->oldstate == TCP_LISTEN)
            op_type = NET_OP_TCP_ACCEPT;
    }
    else if (ctx->newstate == TCP_CLOSE)
    {
        op_type = NET_OP_TCP_CLOSE;
    }

    if (op_type == 0)
        return 0;

    // Prefer PID captured at syscall/kprobe time (stored keyed by socket ptr).
    // Fall back to bpf_get_current_pid_tgid() if no entry exists.
    void *sk = (void *)ctx->skaddr;
    __u32 pid = 0;
    // Use a u64 key that holds the pointer value for map operations.
    __u64 sk_key = (__u64)sk;
    struct sock_info *si = bpf_map_lookup_elem(&sock_pid_map, &sk_key);
    if (si) {
        pid = si->pid;
        // once consumed, delete to avoid leaks
        bpf_map_delete_elem(&sock_pid_map, &sk_key);
        // Emit in-band diagnostic: HIT (code 1)
        emit_diag_event(1, pid, sk);
        diag_inc(1);
    } else {
        __u64 pid_tgid = bpf_get_current_pid_tgid();
        pid = pid_tgid >> 32;
        // Emit in-band diagnostic: MISS (code 2)
        emit_diag_event(2, pid, sk);
        diag_inc(2);
    }

    __u32 saddr = 0;
    __u32 daddr = 0;
    __builtin_memcpy(&saddr, ctx->saddr, sizeof(saddr));
    __builtin_memcpy(&daddr, ctx->daddr, sizeof(daddr));

    // The tracepoint exposes sport/dport in host byte order.
    emit_network_event(pid, saddr, daddr, ctx->sport, ctx->dport,
                       PROTO_TCP, op_type, 0);

    return 0;
}

// Kprobe on tcp_v4_connect to capture the socket pointer in process context.
// This lets us attribute the subsequent inet_sock_set_state event to the
// originating PID even when inet_sock_set_state runs in softirq.
SEC("kprobe/tcp_v4_connect")
int kprobe__tcp_v4_connect(struct pt_regs *ctx)
{
    void *sk = (void *)PT_REGS_PARM1(ctx);
    if (!sk)
        return 0;

    __u64 pid_tgid = bpf_get_current_pid_tgid();
    struct sock_info info = {};
    info.pid = (__u32)(pid_tgid >> 32);
    info.start_ns = bpf_ktime_get_ns();
    __u64 sk_key = (__u64)sk;
    bpf_map_update_elem(&sock_pid_map, &sk_key, &info, BPF_ANY);
    // Emit in-band diagnostic: STORE (code 0)
    emit_diag_event(0, info.pid, sk);
    diag_inc(0);
    return 0;
}

// Also attach to tcp_v6_connect in case kernel uses the IPv6 path for
// dual-stack sockets or different internal call paths.
SEC("kprobe/tcp_v6_connect")
int kprobe__tcp_v6_connect(struct pt_regs *ctx)
{
    void *sk = (void *)PT_REGS_PARM1(ctx);
    if (!sk)
        return 0;

    __u64 pid_tgid = bpf_get_current_pid_tgid();
    struct sock_info info = {};
    info.pid = (__u32)(pid_tgid >> 32);
    info.start_ns = bpf_ktime_get_ns();
    __u64 sk_key = (__u64)sk;
    bpf_map_update_elem(&sock_pid_map, &sk_key, &info, BPF_ANY);
    // Emit in-band diagnostic: STORE (code 0)
    emit_diag_event(0, info.pid, sk);
    diag_inc(0);
    return 0;
}

// Generic tcp_connect symbol as a fallback on kernels that export a common
// connector symbol name.
SEC("kprobe/tcp_connect")
int kprobe__tcp_connect(struct pt_regs *ctx)
{
    void *sk = (void *)PT_REGS_PARM1(ctx);
    if (!sk)
        return 0;

    __u64 pid_tgid = bpf_get_current_pid_tgid();
    struct sock_info info = {};
    info.pid = (__u32)(pid_tgid >> 32);
    info.start_ns = bpf_ktime_get_ns();
    __u64 sk_key = (__u64)sk;
    bpf_map_update_elem(&sock_pid_map, &sk_key, &info, BPF_ANY);
    // Emit in-band diagnostic: STORE (code 0)
    emit_diag_event(0, info.pid, sk);
    diag_inc(0);
    return 0;
}

// The accept path returns a newly-allocated socket pointer; capture it in a
// kretprobe so we can see the return value (the accepted struct sock *).
SEC("kretprobe/inet_csk_accept")
int kretprobe__inet_csk_accept(struct pt_regs *ctx)
{
    void *sk = (void *)PT_REGS_RC(ctx);
    if (!sk)
        return 0;

    __u64 pid_tgid = bpf_get_current_pid_tgid();
    struct sock_info info = {};
    info.pid = (__u32)(pid_tgid >> 32);
    info.start_ns = bpf_ktime_get_ns();
    __u64 sk_key = (__u64)sk;
    bpf_map_update_elem(&sock_pid_map, &sk_key, &info, BPF_ANY);
    return 0;
}

// TCP connect - Outbound connection
struct connect_args {
    unsigned long long unused;
    long syscall_nr;
    int fd;
    struct sockaddr *addr;
    int addrlen;
};

SEC("tracepoint/syscalls/sys_enter_connect")
int trace_connect(struct connect_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    __u32 pid = pid_tgid >> 32;
    
    struct sockaddr_in addr;
    bpf_probe_read_user(&addr, sizeof(addr), ctx->addr);
    
    // Local address is not available at sys_enter_connect time. TCP
    // connection events are emitted from trace_inet_sock_set_state instead.
    (void)pid;
    (void)addr;
    return 0;
}

// TCP accept - Inbound connection
struct accept_args {
    unsigned long long unused;
    long syscall_nr;
    int fd;
    struct sockaddr *addr;
    int *addrlen;
};

SEC("tracepoint/syscalls/sys_enter_accept")
int trace_accept(struct accept_args *ctx)
{
    // Peer/local addresses are not available at sys_enter_accept time. TCP
    // accept events are emitted from trace_inet_sock_set_state instead.
    (void)ctx;
    return 0;
}

// sendto - Send data (TCP/UDP)
struct sendto_args {
    unsigned long long unused;
    long syscall_nr;
    int fd;
    void *buf;
    __u64 len;
    unsigned int flags;
    struct sockaddr *dest_addr;
    int addrlen;
};

SEC("tracepoint/syscalls/sys_enter_sendto")
int trace_sendto(struct sendto_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    __u32 pid = pid_tgid >> 32;
    
    if (ctx->dest_addr)
    {
        struct sockaddr_in addr;
        bpf_probe_read_user(&addr, sizeof(addr), ctx->dest_addr);
        
        if (addr.sin_family == 2)
        {
            emit_network_event(pid, 0, addr.sin_addr, 0,
                              bpf_ntohs(addr.sin_port), PROTO_UDP,
                              NET_OP_UDP_SEND, (__u32)ctx->len);
        }
    }
    // For connected TCP sockets, sys_enter_sendto does not include the local
    // or remote socket addresses. Avoid emitting placeholder 0.0.0.0 TCP
    // events here; TCP connection addresses come from inet_sock_set_state.
    
    return 0;
}

// recvfrom - Receive data (TCP/UDP)
struct recvfrom_args {
    unsigned long long unused;
    long syscall_nr;
    int fd;
    void *buf;
    __u64 len;
    unsigned int flags;
    struct sockaddr *src_addr;
    int *addrlen;
};

SEC("tracepoint/syscalls/sys_enter_recvfrom")
int trace_recvfrom(struct recvfrom_args *ctx)
{
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    __u32 pid = pid_tgid >> 32;
    
    if (ctx->src_addr)
    {
        struct sockaddr_in addr;
        bpf_probe_read_user(&addr, sizeof(addr), ctx->src_addr);
        
        if (addr.sin_family == 2)
        {
            emit_network_event(pid, addr.sin_addr, 0,
                              bpf_ntohs(addr.sin_port), 0, PROTO_UDP,
                              NET_OP_UDP_RECV, 0);
        }
    }
    // For connected TCP sockets, sys_enter_recvfrom does not include the local
    // or remote socket addresses. Avoid emitting placeholder 0.0.0.0 TCP
    // events here; TCP connection addresses come from inet_sock_set_state.
    
    return 0;
}

char LICENSE[] SEC("license") = "GPL";
