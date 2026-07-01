# Wintap Wiki — Schema (dave-wiki)

This is **Dave's per-developer wiki** — the persistent, compounding memory for
this developer, maintained solely by the **Engineer** subagent (see
`.claude/agents/engineer.md`). The Developer reads it but never writes to it. It
implements the Karpathy "LLM wiki" idea: knowledge accretes here across sessions
instead of scattering across chat.

Each developer keeps their own `<name>-wiki/` directory so wiki edits never
collide in git — a developer only ever writes their own directory.

## Layout

| Path | Contents |
|---|---|
| `sources/` | Raw, informal session scratch notes and ingested source material. Immutable-ish inputs. |
| `wiki/decisions/` | ADRs — one file per settled architectural decision (`YYYY-MM-DD-kebab-title.md`). |
| `wiki/entities/` | Entity pages — concrete things in the system (e.g. `wintap-message.md`, `plugin-manager.md`). |
| `wiki/architecture/` | Concept / architecture pages (e.g. `etl-flow.md`, `esper-cep.md`). |
| `wiki/log.md` | Append-at-top chronological log of every Engineer session. |

## Workflow

- **Ingest:** drop a source into `sources/`; the Engineer reads it, updates
  entity/concept pages, notes contradictions, and logs the activity.
- **Decide:** when the Architect settles a decision, the Engineer writes/updates
  an ADR and the affected pages.
- **Reference:** the Developer consults relevant pages (read-only) before
  implementing an approved instruction.

Formats for ADRs, wiki pages, and log entries are defined in
`.claude/agents/engineer.md`.
