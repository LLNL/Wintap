# Wintap Workbench

This directory contains the Angular-based web workbench used by Wintap.

The built frontend is copied into the .NET output during application builds when the workbench `dist/Workbench` directory exists.

## Tech Stack

- Angular 15
- PrimeNG
- SignalR client
- CodeMirror and charting dependencies for interactive views

## Common Commands

Install dependencies:

```bash
npm install
```

Start the frontend dev server:

```bash
npm start
```

Build the workbench:

```bash
npm run build
```

Run unit tests:

```bash
npm test
```

Run lint checks:

```bash
npm run lint
```

## Build Integration

The .NET build copies frontend assets from:

```text
shared/Wintap-Workbench/dist/Workbench
```

into the application output `Workbench/` directory when that build output exists.

If the frontend changes are not appearing in the app output, rebuild the workbench first and then rebuild the .NET project.

## Notes

- This README is intentionally project-specific and replaces the default Angular CLI boilerplate.
- The workbench is part of the larger Wintap build and deployment story; see `../../BUILD_AND_TEST.md` for the backend/runtime side.
