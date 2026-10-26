Precompiled, self-contained Windows packages for the experimental Haltech NSP MCP server.

- **win-arm64.zip:** Windows on ARM.
- **win-x86.zip:** 32-bit Intel/AMD executable (x86 means 32-bit, not x64).
- Each ZIP includes the .NET runtime, a double-click `Start-NspMcp.cmd` launcher, configuration, and guides. No Git or .NET SDK installation required.
- Extract the entire ZIP into a writable folder and read `QUICKSTART.md`. Haltech NSP must be installed separately and run in the same unlocked desktop session.
- Companion `.sha256` files contain SHA-256 checksums. Packages are unsigned.
- The architecture-independent **haltech-nsp-skill** ZIP provides companion operating and recovery instructions for Codex and Claude Code. Install it on the AI client machine using its `INSTALL.md`; it does not install or connect the server.

Both packages pass logic tests and an extracted-package startup check on Windows: executable architecture, self-contained runtime, isolated worker startup, MCP initialization, tool discovery, and the table catalog. The ARM64 package is checked on an ARM64 runner; x86 on an x64 runner. These checks do not exercise a real NSP installation or validate UI editing across architectures.

Use a disposable offline map, disconnect the ECU, and independently verify edits. The server and AI can make mistakes. Keep the default loopback endpoint; no authentication or TLS is implemented. Preserve configuration, logs, and the recovery journal across updates. See the bundled guides for recovery and client setup.
