# UDP Local Tuple `0.0.0.0:0` Notes

## Problem

Some `udppacket` parquet rows show missing local tuple fields:

- `UdpPacket_SourceAddress = 0.0.0.0` and/or `UdpPacket_SourcePort = 0` on `UdpIpSend`
- `UdpPacket_DestinationAddress = 0.0.0.0` and/or `UdpPacket_DestinationPort = 0` on `UdpIpRecv`

This is most visible when UDP events are emitted from syscall tracepoints (`sys_enter_sendto` and the `recvfrom` enter/exit pair).

## What We Have Today

### Working paths

1. **Connected UDP (sendmsg/recvmsg)**
   - Some apps (notably DNS tooling) use `connect()` on a UDP socket and then call `sendmsg/recvmsg`.
   - We added `kprobe/udp_sendmsg` and `kprobe/udp_recvmsg` and (best-effort) read `struct sock->__sk_common` to populate:
     - local IP/port (source/destination depending on direction)
     - remote IP/port
   - Outcome: for these callers, the tuple is generally complete.

2. **Unconnected UDP using recvfrom**
    - We fixed `UdpIpRecv` by emitting in `kretprobe/udp_recvmsg` using the user-provided peer buffer:
      - read peer from `msghdr->msg_name` (recvmsg/recvmmsg) or `recvfrom()`'s `src_addr` pointer
      - read destination tuple (local IP/port) from `struct sock`
    - The `sys_exit_recvfrom` tracepoint is now cleanup-only (avoid emitting partial rows).
    - Outcome: `UdpIpRecv` peer and destination tuples are populated when the caller provides an address buffer.

### Still incomplete

1. **Unconnected UDP using sendto**
   - `sys_enter_sendto` provides only the destination sockaddr.
   - The local tuple is not known at syscall entry (the ephemeral port may not be assigned yet, and local IP depends on routing/bind).
    - Current emission uses only the destination sockaddr (by design), hence `0.0.0.0:0` local tuple.

2. **Sockets bound to INADDR_ANY**
   - Even for connected sockets, the kernel may report `skc_rcv_saddr == 0` (bound to 0.0.0.0).
   - This can be “correct” from the socket API perspective, but it is not the desired “effective local IP” for telemetry.

## Why This Happens (Mechanics)

- For UDP, unless you are inspecting the socket object itself, the syscall interface often does not carry a complete tuple.
- On `sendto()`:
  - destination is known (passed by user)
  - local port may be allocated during or after the send
  - local IP may be implicit (routing choice) and can remain `INADDR_ANY` from the socket’s bind state
- On `recvfrom()`:
  - source address is written into user memory on return
  - destination tuple is the local socket tuple, which is not provided by the syscall args

## Observations From Validation

- `dig` uses connected UDP (`connect + sendmsg/recvmsg`) when talking to the local stub resolver (`127.0.0.53`).
- When we trace that path, we can usually populate local port, and often a concrete local IP.
- When tracing the syscall `sendto/recvfrom` path, `0.0.0.0:0` is expected with the current design because we are not correlating to a socket.

## Options (On Hold)

We are intentionally putting these follow-ups on hold for now, but these are the likely approaches:

1. **FD -> socket correlation in syscalls**
   - Use `bpf_sockfd_lookup()` (if available) at syscall time to get `struct sock *` from `fd`.
   - Then read local/remote tuple from the socket.
   - Risks: helper availability/kernel support; lifetime management; verifier constraints.

2. **Use existing tracepoints with socket pointer**
   - Tracepoints like `sock:sock_send_length` / `sock:sock_recv_length` carry `sk`, `family`, and `protocol` plus a length.
   - These can be used as a stable place to capture tuple from the socket object (still requires reading socket fields).
   - Risks: these tracepoints may not include UDP in some kernels (protocol print list is TCP/SCTP/MPTCP), so validation required.

3. **Infer “effective local IP”**
   - For loopback traffic, infer `127.0.0.1` when the peer is loopback.
   - For non-loopback, inferring the egress IP properly requires routing information and is non-trivial without extra kernel helpers.

## Where To Look In Code

- Tracer: `wintap/platform/linux/sensor/ebpf/tracers/network_ops_tracer.bpf.c`
   - `trace_sendto` (currently only destination tuple for UDP)
   - `trace_recvfrom` (stashes user pointers for recvfrom)
   - `kprobe__udp_recvmsg` + `kretprobe__udp_recvmsg` (peer sockaddr + local tuple for recv)
   - `kprobe__udp_sendmsg` (connected UDP tuple population)

- Userland: `wintap/platform/linux/sensor/ebpf/NetworkSensor.cs`
  - Maps `NET_OP_UDP_SEND/NET_OP_UDP_RECV` into `UdpIpSend/UdpIpRecv` and writes `WintapMessage.UdpPacket`.
