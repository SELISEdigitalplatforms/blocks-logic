# Security Policy

## Reporting a Vulnerability

At **blocks-workflow-next-sub**, we take security seriously. If you discover a security vulnerability, please follow the guidelines below to report it responsibly.

### How to Report

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, send an email to **blocks@selisegroup.com** with the following details:

- **Subject**: `[SECURITY] blocks-workflow-next-sub - Brief Description`
- **Description**: A clear and concise description of the vulnerability.
- **Steps to Reproduce**: Detailed steps to reproduce the vulnerability.
- **Impact**: The potential impact of the vulnerability.
- **Suggested Fix**: If you have a suggested fix, please include it.

### What to Expect

- **Acknowledgment**: We will acknowledge receipt of your report within **48 hours**.
- **Assessment**: We will assess and validate the vulnerability within **7 business days**.
- **Resolution**: We will work on a fix and aim to resolve critical vulnerabilities within **30 days**.
- **Notification**: Once the vulnerability is resolved, we will notify you and credit you in the release notes (if desired).

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| Latest  | :white_check_mark: |
| Older   | :x:                |

## Security Best Practices

When integrating **blocks-workflow-next-sub**, please adhere to the following best practices:

- Always use the latest version of the module.
- Ensure `NEXT_PUBLIC_API_BASE_URL` points only to trusted, TLS-secured endpoints.
- Do not expose workflow node configurations (which may contain API keys or credentials set in `httpRequest` nodes) in client-side logs or error messages.
- Validate and sanitise expression templates (`{{node.output.field}}`) server-side before execution; never execute untrusted expressions in the browser without sanitisation.
- Restrict workflow activation/deactivation to appropriately authorised users; enforce access control at the API layer.
- Rotate API keys and credentials stored in workflow node configurations regularly.
- Monitor execution logs for unusual patterns in `httpRequest` or `agent` node invocations.

## Disclosure Policy

We are committed to responsible disclosure. Once a vulnerability is confirmed and patched, we will publicly disclose the details in a security advisory to inform the community.

Thank you for helping keep **blocks-workflow-next-sub** secure!
