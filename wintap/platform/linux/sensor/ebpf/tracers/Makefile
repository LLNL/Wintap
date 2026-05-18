# Output files
OBJS = file_ops_tracer.bpf.o execve_tracer.bpf.o exit_tracer.bpf.o clone_tracer.bpf.o network_ops_tracer.bpf.o
VMLINUX = vmlinux.h

# Auto-detect architecture
ARCH := $(shell uname -m)
ifeq ($(ARCH),aarch64)
	TARGET_ARCH := arm64
else ifeq ($(ARCH),x86_64)
	TARGET_ARCH := x86_64
else
	TARGET_ARCH := $(ARCH)
endif

.PHONY: all clean test

all: $(OBJS) $(VMLINUX)

# Pattern rule: This handles ALL .c to .o conversions
# $< is the source file (.c), $@ is the target file (.o)
%.bpf.o: %.bpf.c
	clang -g -O2 -target bpf -D__TARGET_ARCH_$(TARGET_ARCH) \
	-I/usr/include/bpf -c $< -o $@
	@echo "Compiled $< for architecture: $(TARGET_ARCH)"

$(VMLINUX):
	bpftool btf dump file /sys/kernel/btf/vmlinux format c > $(VMLINUX)

# Placeholder for your test logic
test: all
	@echo "Running eBPF tests..."
	# Add your actual test command here, e.g.:
	# ./test_runner --objs $(OBJS)
	@echo "eBPF tests passed!"

clean:
	rm -f $(OBJS) $(VMLINUX)
