.PHONY: all clean fileops execve exit clone network

# Output files
FILEOPS_BPF_OBJ = file_ops_tracer.bpf.o
EXECVE_BPF_OBJ = execve_tracer.bpf.o
EXIT_BPF_OBJ = exit_tracer.bpf.o
CLONE_BPF_OBJ = clone_tracer.bpf.o
NETWORK_BPF_OBJ = network_ops_tracer.bpf.o
VMLINUX = vmlinux.h

all: $(FILEOPS_BPF_OBJ) $(EXECVE_BPF_OBJ) $(EXIT_BPF_OBJ) $(CLONE_BPF_OBJ) $(NETWORK_BPF_OBJ)

fileops: $(FILEOPS_BPF_OBJ)
execve: $(EXECVE_BPF_OBJ)
exit: $(EXIT_BPF_OBJ)
clone: $(CLONE_BPF_OBJ)
network: $(NETWORK_BPF_OBJ)

# Auto-detect architecture
ARCH := $(shell uname -m)
ifeq ($(ARCH),aarch64)
    TARGET_ARCH := arm64
else ifeq ($(ARCH),x86_64)
    TARGET_ARCH := x86_64
else
    TARGET_ARCH := $(ARCH)
endif

# Generate vmlinux.h with BTF information
$(VMLINUX):
	bpftool btf dump file /sys/kernel/btf/vmlinux format c > $(VMLINUX)

# Compile fileops eBPF program
$(FILEOPS_BPF_OBJ): file_ops_tracer.bpf.c
	clang -g -O2 -target bpf -D__TARGET_ARCH_$(TARGET_ARCH) \
		-I/usr/include/bpf \
		-c file_ops_tracer.bpf.c -o $(FILEOPS_BPF_OBJ)
	@echo "Compiled fileops tracer for architecture: $(TARGET_ARCH)"

# Compile execve eBPF program
$(EXECVE_BPF_OBJ): execve_tracer.bpf.c
	clang -g -O2 -target bpf -D__TARGET_ARCH_$(TARGET_ARCH) \
		-I/usr/include/bpf \
		-c execve_tracer.bpf.c -o $(EXECVE_BPF_OBJ)
	@echo "Compiled execve tracer for architecture: $(TARGET_ARCH)"

# Compile exit eBPF program
$(EXIT_BPF_OBJ): exit_tracer.bpf.c
	clang -g -O2 -target bpf -D__TARGET_ARCH_$(TARGET_ARCH) \
		-I/usr/include/bpf \
		-c exit_tracer.bpf.c -o $(EXIT_BPF_OBJ)
	@echo "Compiled exit tracer for architecture: $(TARGET_ARCH)"

# Compile clone eBPF program
$(CLONE_BPF_OBJ): clone_tracer.bpf.c
	clang -g -O2 -target bpf -D__TARGET_ARCH_$(TARGET_ARCH) \
		-I/usr/include/bpf \
		-c clone_tracer.bpf.c -o $(CLONE_BPF_OBJ)
	@echo "Compiled clone tracer for architecture: $(TARGET_ARCH)"

# Compile network_ops eBPF program
$(NETWORK_BPF_OBJ): network_ops_tracer.bpf.c
	clang -g -O2 -target bpf -D__TARGET_ARCH_$(TARGET_ARCH) \
		-I/usr/include/bpf \
		-c network_ops_tracer.bpf.c -o $(NETWORK_BPF_OBJ)
	@echo "Compiled network_ops tracer for architecture: $(TARGET_ARCH)"

clean:
	rm -f $(FILEOPS_BPF_OBJ) $(EXECVE_BPF_OBJ) $(EXIT_BPF_OBJ) $(CLONE_BPF_OBJ) $(NETWORK_BPF_OBJ) $(VMLINUX)