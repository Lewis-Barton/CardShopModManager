# Contributing

Thanks for your interest in **TCG Card Shop Sim Mod Manager**.

This project is still young and, frankly, being written as a learning exercise,
so don't be surprised if things shift around between releases. That said, bug
reports and pull requests are welcome.

## Reporting a problem

Open an issue and fill in the bug template. Tell me what you did, what you
expected, and what actually happened. If you can, attach a support bundle — run
`TCGCardShopSimModManager.Cli support-bundle` and share the zip it produces. It
collects logs and environment info **without** including your Nexus API key.

## Opening a pull request

- Keep changes focused: one thing per PR.
- Run `dotnet build` and `dotnet test` before pushing. Both should come back
  clean (no warnings, all tests green).
- Match the surrounding code. There's no auto-formatter enforced yet, so just
  follow the style already in the file you're editing.
- Write the commit message like a human would: short, imperative, describing the
  *why*, not the *what*.

## A note on the Nexus API key

The Nexus Mods API key is stored only on your machine (DPAPI-encrypted under
your user profile) and never leaves it except to talk to Nexus's own API over
HTTPS. It is not part of this repository. If you touch anything under
`src/TCGCardShopSimModManager.Core/Nexus*` or the key store, be careful not to
add a code path that could write the key into a log or a committed file — the
`.gitignore` is set up to keep key material out, but don't rely on that alone.

## Code of conduct

By participating, you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).
