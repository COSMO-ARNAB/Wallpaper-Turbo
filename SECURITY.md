# Security Policy

## Supported Versions

We actively monitor and provide security patches for the following versions of **Wallpaper Turbo**:

| Version | Supported | Notes |
| ------- | --------- | ----- |
| v1.1.x  | ✅ Yes     | Active main production branch. |
| < v1.1  | ❌ No      | Legacy/unstable MVP versions. Please upgrade to the latest release. |

---

## Reporting a Vulnerability

**DO NOT open a public GitHub issue for security vulnerabilities.** Publicly disclosing a vulnerability makes your system and other users' computers vulnerable to exploitation before a fix is available.

If you discover a security bug, memory overflow vulnerability, or exploit in **Wallpaper Turbo**, please report it responsibly by following these steps:

1. **Email the developer privately** at: **security-reports@arnab.dev** (or open a private security draft under the "Security" tab of the GitHub repository).
2. **Include detailed information** in your report:
   - A clear description of the vulnerability.
   - A step-by-step proof of concept (PoC) to reproduce the issue.
   - The impact of the vulnerability (e.g., local crash, memory leak, or privilege escalation).
3. **Response Time**: We take security issues very seriously and will evaluate and respond to your report within **48 hours**.
4. **Coordinated Disclosure**: We ask that you do not publish the vulnerability details until we have successfully deployed a security patch to protect the user base.

---

## Security Practices

* **Memory Safety**: We constantly audit and profile memory allocations in the background WPF and VLC pipelines to prevent buffer overflows.
* **No Telemetry**: Since Wallpaper Turbo runs entirely offline and stores no personal data, there is no remote transfer vector for user data.
