# Linux eBPF BTF/CO-RE Deployment Handoff

## Purpose

This document tracks Wintap's Linux eBPF `vmlinux.h`, BTF, CO-RE, and cross-kernel deployment strategy. It is intended for both human maintainers and coding agents.

## Current Goal

Build Linux sensors that are deployable across a wide range of Linux distributions, kernel versions, and CPU architectures without requiring every production endpoint to have a full eBPF build toolchain.

## Key Concepts

- `vmlinux.h` is generated from kernel BTF, usually `/sys/kernel/btf/vmlinux`, using `bpftool`.
- `vmlinux.h` is not provided by normal kernel development headers because it represents compiled kernel internal type layouts, not public UAPI headers.
- CO-RE eBPF objects are compiled with BTF type metadata and relocated by libbpf at load time against the target kernel's BTF.
- With CO-RE, the build-time `vmlinux.h` does not have to exactly match the target kernel version, but the target kernel must expose compatible types/fields for the BPF program's relocations.
- UAPI headers such as `<linux/bpf.h>` and BTF-generated `vmlinux.h` must not be mixed in the same BPF C file because they can define the same types and enums.

## Current Repository State

- Tracers live in `wintap/platform/linux/sensor/ebpf/tracers`.
- `execve_tracer.bpf.c` and `exit_tracer.bpf.c` use CO-RE (`vmlinux.h` plus `BPF_CORE_READ`).
- `execve_tracepoint.bpf.c` and `exit_tracepoint.bpf.c` are tracepoint-only fallbacks for systems where CO-RE/BTF loading fails.
- Several tracepoint-only tracers use UAPI headers and handwritten tracepoint context structures.
- `network_ops_tracer.bpf.c` now uses CO-RE for `struct sock` tuple reads instead of handwritten `struct sock` / `sock_common` layout fragments.
- `network_tracepoint.bpf.c` is a reduced network fallback that captures `inet_sock_set_state`, `sendto`, and `recvfrom` data without CO-RE socket layout reads.
- File activity is covered by tracepoint-only file activity objects. `file_ops_tracer.bpf.o` remains the default and `file_ops_tracepoint.bpf.o` is an explicit fallback variant with the same event ABI.
- `ProcessRundownSensor` is a non-eBPF fallback path that enumerates `/proc` and emits process refresh events.

## Known Failure Signature

When a BPF C file includes both `vmlinux.h` and `<linux/bpf.h>`, builds can fail with errors like:

```text
typedef redefinition with different types ('struct __kernel_fd_set' vs 'struct __kernel_fd_set')
redefinition of 'bpf_insn'
redefinition of enumerator 'BPF_REG_0'
```

This is include hygiene failure, not necessarily a bad kernel package.

## Build Strategy

### Development / CI Build

Generate `vmlinux.h` from the current kernel BTF into a build directory:

```bash
bpftool btf dump file /sys/kernel/btf/vmlinux format c > .build/<arch>-<kernel>/vmlinux.h
```

Then compile tracer objects with `clang -target bpf` and `-I.build/<arch>-<kernel>`.

Do not check generated `vmlinux.h` into source as the implicit build input.

### Production Build

Prefer shipping prebuilt CO-RE BPF objects per architecture:

```text
tracers/linux-x64/*.bpf.o
tracers/linux-arm64/*.bpf.o
```

Production endpoints should not need `clang` or `bpftool` unless they opt into local compilation.

### Runtime Load Strategy

At runtime, the sensor manager should:

- detect architecture;
- detect whether `/sys/kernel/btf/vmlinux` exists;
- attempt to load the richest CO-RE object first;
- if load/relocation/attach fails, log the object path and libbpf return code;
- fall back to a simpler object or non-eBPF sensor where available;
- never let one failed eBPF sensor crash the entire sensor subsystem.

Current fallback object order:

| Sensor | Primary Object | Fallback Object |
| --- | --- | --- |
| Execve | `execve_tracer.bpf.o` | `execve_tracepoint.bpf.o` |
| Exit | `exit_tracer.bpf.o` | `exit_tracepoint.bpf.o` |
| Network | `network_ops_tracer.bpf.o` | `network_tracepoint.bpf.o` |
| FileOps | `file_ops_tracer.bpf.o` | `file_ops_tracepoint.bpf.o` |
| Clone | `clone_tracer.bpf.o` | none yet; already tracepoint-only |
| OpenAt | `openat_tracer.bpf.o` | none; diagnostic/simple tracepoint sensor |

## Tracer Include Rules

### CO-RE Tracers

Use this pattern:

```c
#include "vmlinux.h"
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>
#include <bpf/bpf_core_read.h>
```

Do not include `<linux/bpf.h>`, `<linux/ptrace.h>`, or other kernel/UAPI headers unless proven non-conflicting.

### Tracepoint/UAPI Tracers

Use this pattern:

```c
#include <linux/bpf.h>
#include <bpf/bpf_helpers.h>
#include <bpf/bpf_tracing.h>
```

Do not include `vmlinux.h` in these files unless converting the tracer to CO-RE.

## Sensor Robustness Tiers

### Process Sensor

- Tier 1: CO-RE exec/exit/fork with parent enrichment from kernel types.
- Tier 0: tracepoint-only process events with userspace `/proc` enrichment (`execve_tracepoint.bpf.o`, `exit_tracepoint.bpf.o`, `clone_tracer.bpf.o`).
- Fallback: `ProcessRundownSensor` scans `/proc` and emits refresh events.

### File Sensor

- Tier 0 remains syscall tracepoint based and includes open/read/write/close/mmap/unlink activity.
- CO-RE should only be used if richer kernel-internal context is necessary.

### Network Sensor

- Tier 1 should use CO-RE for `struct sock` reads.
- Tier 0 avoids kernel struct layout assumptions and provides reduced context through `network_tracepoint.bpf.o`.
- Handwritten `struct sock` layouts are not robust and should be isolated behind fallback labeling.

## Compatibility Matrix To Maintain

For each tested platform, record:

- distribution and version;
- kernel release (`uname -r`);
- architecture (`uname -m`);
- `/sys/kernel/btf/vmlinux` present (`yes`/`no`);
- `bpftool` present (`yes`/`no`);
- `clang` version;
- `libbpf` version;
- each BPF object build result;
- each sensor runtime load/attach result;
- fallback path used, if any.

## Current Recommendations

1. Keep generated `vmlinux.h` out of source and generate it into a build directory.
2. Fix include hygiene before debugging kernel package issues.
3. Prebuild production CO-RE objects per architecture.
4. Add runtime sensor fallback reporting and treat rich eBPF data as optional when portability requires it.
5. Keep network kernel-struct reads on CO-RE and add a reduced tracepoint-only fallback object if old/no-BTF systems must be supported.

## Useful Commands

```bash
uname -m
uname -r
test -r /sys/kernel/btf/vmlinux && echo BTF_PRESENT
bpftool btf dump file /sys/kernel/btf/vmlinux format c > /tmp/vmlinux.h
make -C wintap/platform/linux/sensor/ebpf/tracers clean all
```

## Agent Notes

- When an eBPF build fails, inspect include collisions first.
- Do not assume kernel headers solve CO-RE type layout problems.
- Do not add generated `vmlinux.h` back as the default source-tree header.
- If adding fallback objects, keep filenames explicit (for example `execve_core.bpf.o`, `execve_tracepoint.bpf.o`) and update `BaseEbpfSensor` fallback lists.
