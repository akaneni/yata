**English** · [Português](CONTRIBUTING.pt-BR.md)

# Contributing to Yata

Thanks for wanting to help. Bug reports, fixes and translations are the most welcome, and forks are fine: if you built something on your own copy, open an issue and tell us about it — good fixes from forks get brought back, with credit.

## Before you start

- **Bug?** Open an issue with what happened, what you expected, your Windows version and, if you can, the log from `%LOCALAPPDATA%\Yata\logs` (it holds no songs, tasks, events, paths or accounts).
- **New feature?** Open an issue first and describe the idea. Yata is meant to stay small: a feature has a better chance if it fits what the notch already is.
- **Translation?** See [Translations](README.md#translations) in the README.

## What fits Yata

- **Minimal.** One thing done well beats five options. Monochrome; the album art's color is the only accent.
- **Private by default.** No account, no telemetry. Anything that goes to the network is opt-in, sends the least it can, and is listed in [PRIVACY.md](PRIVACY.md). Yata doesn't read other apps' data (notifications, clipboard, files) behind the user's back.
- **Light.** The notch is drawn on the CPU: every new animation or bigger window costs frames. Measure before and after.
- **Yata is the raven.** No other mascots or characters.

## Pull requests

- **One subject per PR.** A fix, a feature or a cleanup — not all three. Big PRs mixing several things can't be reviewed and will be asked to be split.
- **Tests** for logic that can be tested without a window (see `tests/Yata.Tests`).
- **It has to pass what CI runs:**

  ```powershell
  dotnet build -c Release -warnaserror
  dotnet test -c Release
  ```

- **Every visible text** goes in `Strings.resx` and `Strings.pt-BR.resx`.
- Don't bump the version or edit the CHANGELOG: that's done in the release PR.

## Official sources

The official repository is [github.com/akaneni/yata](https://github.com/akaneni/yata) and the site is [getyata.app](https://getyata.app); official installers are built by GitHub Actions from this repository and can be checked with `gh attestation verify` (see the README). If you publish builds from a fork, please make it clear they're not the official ones.

By contributing, you agree your contribution is licensed under the [MIT License](LICENSE).
