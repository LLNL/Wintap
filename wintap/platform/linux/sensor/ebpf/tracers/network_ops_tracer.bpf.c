#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>

// Network byte order conversion
#define bpf_ntohs(x) __builtin_bswap16(x)

#define TASK_COMM_LEN 16

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

// sockaddr_in structure (IPv4)
struct sockaddr_in {
    __u16 sin_family;
    __u16 sin_port;
    __u32 sin_addr;
};

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
    
    // Only handle IPv4 (family = 2)
    if (addr.sin_family == 2)
    {
        emit_network_event(pid, 0, addr.sin_addr, 0, 
                          bpf_ntohs(addr.sin_port), PROTO_TCP,
                          NET_OP_TCP_CONNECT, 0);
    }
    
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
    __u64 pid_tgid = bpf_get_current_pid_tgid();
    __u32 pid = pid_tgid >> 32;
    
    if (ctx->addr)
    {
        struct sockaddr_in addr;
        bpf_probe_read_user(&addr, sizeof(addr), ctx->addr);
        
        if (addr.sin_family == 2)
        {
            emit_network_event(pid, addr.sin_addr, 0, 
                              bpf_ntohs(addr.sin_port), 0, PROTO_TCP,
                              NET_OP_TCP_ACCEPT, 0);
        }
    }
    
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
    else
    {
        // TCP send (connected socket, no dest_addr)
        emit_network_event(pid, 0, 0, 0, 0, PROTO_TCP,
                          NET_OP_TCP_SEND, (__u32)ctx->len);
    }
    
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
    else
    {
        // TCP receive
        emit_network_event(pid, 0, 0, 0, 0, PROTO_TCP,
                          NET_OP_TCP_RECV, 0);
    }
    
    return 0;
}

char LICENSE[] SEC("license") = "GPL";
