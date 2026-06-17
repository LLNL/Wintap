# Contributing to Wintap

Wintap is an open-source LLNL project. Contributions are welcome in the form of bug reports, design discussion, documentation improvements, and code changes.

All contributions are made under the MIT license used by this repository.

## Before You Start

- Search for existing issues, pull requests, or design notes related to your change.
- Open an issue or discussion first for larger changes, new features, or cross-cutting refactors.
- Prefer small, reviewable pull requests over large mixed changes.

## Development Workflow

1. Fork the repository or create a topic branch if you already have push access.
2. Branch from the repository's current default branch or the base branch requested by maintainers.
3. Make focused changes.
4. Run the relevant build, test, or smoke-test steps.
5. Update documentation when behavior, commands, or operational guidance changes.
6. Open a pull request with a clear summary of what changed and how it was validated.

## Branch Naming

Use descriptive branch names, for example:

- `feature/<short-name>`
- `fix/<short-name>`
- `docs/<short-name>`
- `spike/<short-name>`

## Pull Request Expectations

Include the following in your pull request description:

- the problem being solved
- the scope of the change
- any user-visible behavior changes
- the commands or tests you ran
- follow-up work that is intentionally out of scope

## Documentation Expectations

If your change affects any of the following, update the corresponding docs in the same pull request:

- build or run commands
- deployment steps
- environment variables
- troubleshooting guidance
- smoke-test or validation workflows

The highest-signal operational docs today are:

- [`../README.md`](../README.md)
- [`../BUILD_AND_TEST.md`](../BUILD_AND_TEST.md)
- [`../devtools/README.md`](../devtools/README.md)

## Validation Guidance

Run the narrowest useful validation for your change.

Examples:

- Documentation-only changes: verify links, commands, and paths against the repo
- Linux build changes: `make -C wintap/wintap build_dotnet` and, if relevant, `make -C wintap/wintap build_ebpf`
- Linux runtime changes: `make -C wintap/wintap run-env` plus the relevant smoke tests
- eBPF or telemetry changes: update the related handoff or diagnostic notes when they materially change investigation state

## Reporting Bugs

Useful bug reports usually include:

- host OS and version
- kernel version for Linux issues
- whether the repo is on a native filesystem or a host-shared mount
- exact command run
- relevant log excerpts
- whether the issue reproduces with isolation flags such as `WINTAP_DISABLE_ETL`, `WINTAP_DISABLE_SENSORS`, or per-sensor `WINTAP_ENABLE_*` settings
