# Configuration

This folder will contain checked-in examples with project-relative placeholders. Machine-specific executable paths belong in an ignored local configuration file created during setup.

Configuration must define allowed roots and must never accept `$HOME`, the filesystem root, or an unresolved environment variable as a destructive target.
