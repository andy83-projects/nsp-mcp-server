# Security and trust boundary

This experimental server gives clients access to native NSP desktop actions. It has **no authentication, authorization system, or TLS**. The public source defaults to `127.0.0.1`; keep it there and use an SSH tunnel for remote access, or explicitly restrict private-network access to a trusted client. Never expose the endpoint to the internet or an untrusted/shared network.

The worker-generation token isolates the private worker endpoint. It is not authentication for the public MCP endpoint. Tool annotations, confirmation arguments, AI instructions, range clamps, and offline checks do not replace network access controls or human verification. Treat anyone who can reach the public port as able to request powerful application actions.

Do not publish native maps, calibration values, audit logs, screenshots, recovery journals, account details, private keys, or client configuration containing credentials. Redact diagnostic reports before sharing them.

If you find a security issue, use GitHub's private vulnerability-reporting facility when available. Do not disclose credentials, private logs, exploit details, or vehicle data in a public issue. This project has no guaranteed response time or security support commitment.
