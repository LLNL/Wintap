# NEsper Fedora Repro

This standalone repro isolates Fedora NEsper compile/deploy behavior from Lintap service startup, ETL workers, sensors, DuckDB, and plugins.

Run from the repo root:

```bash
dotnet build diagnostics/nesper-repro/nesper-repro.csproj
dotnet diagnostics/nesper-repro/bin/Debug/net8.0/nesper-repro.dll
```

On the Fedora VM shared mount, the repro fails even for:

```text
SELECT * FROM SimpleEvent
```

with:

```text
EPCompileException: Bad IL range
```

Copying the repro and `shared/WintapAPI` to native `/tmp/opencode` and building there makes all queries pass. Running the native-built DLL from the shared repo current directory also passes, while running the shared-built DLL from native `/tmp/opencode` still fails. This narrows the cause to assemblies/output loaded from the shared mount, not current working directory.
