// Simple ring buffer consumer using libbpf
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <bpf/libbpf.h>
#include <bpf/bpf.h>
#include <signal.h>
#include <inttypes.h>

static volatile sig_atomic_t stop = 0;
static void handle_sig(int signo) { stop = 1; }

struct network_event {
    uint32_t pid;
    char comm[16];
    uint64_t timestamp_ns;
    uint32_t saddr;
    uint32_t daddr;
    uint16_t sport;
    uint16_t dport;
    uint8_t protocol;
    uint8_t op_type;
    uint32_t bytes;
    uint8_t is_ipv6;
    uint8_t saddr_v6[16];
    uint8_t daddr_v6[16];
};

static int handle_event(void *ctx, void *data, size_t size)
{
    if (size < sizeof(struct network_event)) {
        fprintf(stderr, "short event: %zu\n", size);
        return 0;
    }
    struct network_event *e = (struct network_event *)data;
    if (e->protocol == 0xFF) {
        printf("DIAG code=%u pid=%u sk_lo=0x%08x comm=%s\n", e->op_type, e->pid, (uint32_t)(e->bytes), e->comm);
    } else {
        printf("EVENT pid=%u comm=%s proto=%u op=%u %u.%u.%u.%u:%u -> %u.%u.%u.%u:%u bytes=%u\n",
               e->pid, e->comm, e->protocol, e->op_type,
               (e->saddr)&0xff, (e->saddr>>8)&0xff, (e->saddr>>16)&0xff, (e->saddr>>24)&0xff, e->sport,
               (e->daddr)&0xff, (e->daddr>>8)&0xff, (e->daddr>>16)&0xff, (e->daddr>>24)&0xff, e->dport,
               e->bytes);
    }
    return 0;
}

int main(int argc, char **argv)
{
    const char *obj_path = "network_ops_tracer.bpf.o";
    if (argc > 1) obj_path = argv[1];

    int map_fd = -1;
    struct bpf_object *obj = NULL;

    // If arg is of form mapid:<id> try to open existing map by id instead of
    // loading the object file. This avoids re-loading programs in a running
    // system and allows attaching to an already-loaded events map.
    if (strncmp(obj_path, "mapid:", 6) == 0) {
        unsigned long id = strtoul(obj_path + 6, NULL, 10);
        // libbpf provides bpf_map_get_fd_by_id() to obtain a map fd.
        map_fd = bpf_map_get_fd_by_id(id);
        if (map_fd < 0) {
            fprintf(stderr, "failed to get map fd for id=%lu\n", id);
            return 1;
        }
    } else {
        obj = bpf_object__open(obj_path);
        if (!obj) {
            fprintf(stderr, "failed to open bpf object: %s\n", obj_path);
            return 1;
        }

        if (bpf_object__load(obj)) {
            fprintf(stderr, "failed to load bpf object\n");
            bpf_object__close(obj);
            return 1;
        }

        struct bpf_map *map = bpf_object__find_map_by_name(obj, "events");
        if (!map) {
            fprintf(stderr, "events map not found\n");
            bpf_object__close(obj);
            return 1;
        }

        map_fd = bpf_map__fd(map);
        if (map_fd < 0) {
            fprintf(stderr, "bad map fd\n");
            bpf_object__close(obj);
            return 1;
        }
    }

    signal(SIGINT, handle_sig);
    signal(SIGTERM, handle_sig);

    struct ring_buffer *rb = ring_buffer__new(map_fd, handle_event, NULL, NULL);
    if (!rb) {
        fprintf(stderr, "failed to create ring buffer\n");
        if (obj) bpf_object__close(obj);
        return 1;
    }

    printf("Listening to ring buffer events (obj=%s). Ctrl-C to stop.\n", obj_path);
    while (!stop) {
        int ret = ring_buffer__poll(rb, 1000 /*ms*/);
        if (ret < 0) {
            fprintf(stderr, "ring buffer poll error: %d\n", ret);
            break;
        }
    }

    ring_buffer__free(rb);
    if (obj) bpf_object__close(obj);
    return 0;
}
