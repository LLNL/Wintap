# Configuration
EBPF_DIR = platform/linux/sensor/ebpf/tracers
DOTNET_PROJ = Lintap.csproj

.PHONY: all clean build_ebpf build_dotnet test_ebpf test_all

# 1. The "Build Everything" target
all: build_ebpf build_dotnet

# 2. Build the eBPF components
build_ebpf:
	@echo "--- Building eBPF Tracers ---"
	$(MAKE) -C $(EBPF_DIR) all

# 3. Build the .NET components
build_dotnet:
	@echo "--- Building .NET Project ---"
	dotnet build $(DOTNET_PROJ)

# 4. Run eBPF tests
test_ebpf:
	@echo "--- Running eBPF Tests ---"
	$(MAKE) -C $(EBPF_DIR) test

# 5. Run everything (eBPF tests + full build)
test_all: test_ebpf build_ebpf build_dotnet

# 6. Clean everything
clean:
	@echo "--- Cleaning eBPF ---"
	$(MAKE) -C $(EBPF_DIR) clean
	@echo "--- Cleaning .NET (with forced restore) ---"
	# We run restore first to satisfy the SDK's dependency check, 
	# then we clean.
	dotnet restore $(DOTNET_PROJ) || true
	dotnet clean $(DOTNET_PROJ)