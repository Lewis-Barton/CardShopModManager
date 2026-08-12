# Security policy

## Supported versions

This is pre-1.0 software. Only the latest release on the `main` branch is
supported — there are no backported fixes for older versions.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Use GitHub's private vulnerability reporting instead: open the **Security** tab
on the repository and choose **Report a vulnerability**. That goes only to the
maintainer.

If you can't use that, email the maintainer directly (address on the profile)
with the subject prefixed `security:`.

## What counts as sensitive here

- The Nexus Mods API key is stored encrypted on your local machine and is only
  ever sent to Nexus's own API over HTTPS. It is not part of this repo.
- `support-bundle` intentionally excludes the key.
- A way to read, exfiltrate, or weaken the protection on that key — or any path
  that writes it into a log or a committed file — is a vulnerability.

## In scope

This tool installs files into a game directory and downloads mod archives.
Reports about unsafe archive extraction, hash/signature bypass, or anything that
could let a malicious manifest damage a user's game folder are all in scope.
