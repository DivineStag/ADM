# GitHub downloads, conventional setup and updates

The current delivery is a normal **ADM-Setup.exe** Windows wizard built with Inno Setup. It includes the full application and runtime; pilots do not run PowerShell scripts, extract payload folders or install the SDK/.NET. It adds Start/Apps entries, offers an optional desktop shortcut, keeps personal profiles/history and caches setup for repair.

## GitHub approach

Use a public GitHub repository and **Releases** for the installer binary. Source hosting and release downloads are separate: downloading an executable does not require the pilot to clone source or have a GitHub account. A stable link of the form `https://github.com/OWNER/REPOSITORY/releases/latest/download/ADM-Setup.exe` always downloads the latest published stable installer. This is preferable to a second downloader/bootstrap executable: first installation can work offline after download, with one executable and a visible setup wizard. It satisfies the latest-version download without a bespoke installation script. See [GitHub release download links](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases).

The owner provided https://github.com/DivineStag/ADM. Build with:

```powershell
.\scripts\Publish-Installer.ps1 -Repository 'DivineStag/ADM' -Version '0.8.0'
```

The repository identity is baked into the application. There is no editable update URL in a pilot's Settings, no token, no external command received from a release description, and no dependency on the developer SDK on a pilot's PC. The local origin and default updater repository are configured as **DivineStag/ADM**. The public repository initially had no commits or releases. Source upload is separate from binary release approval. Existing public-release licensing gates still apply.

## Update behaviour

Configured builds check GitHub's latest stable release at startup, quietly. No configured repository means no request. Failure or no Internet leaves ADM usable. Under Settings → Help & support, users can recheck and choose Install update. Drafts/prereleases, old/same versions and missing/ambiguous installer assets are not accepted. Stable v0.8.0 is newer than 0.8.0-beta.2. Private/beta release channels are not implemented.

Downloads must belong to the baked repository/tag, use HTTPS and have GitHub's SHA-256 asset digest and exact declared size (maximum 500 MiB). Redirects are restricted to GitHub asset hosts; partial or corrupt downloads are rejected. The application finishes handling before enabling installation and checks again after download. It closes its connections before starting the familiar setup wizard. It never silently replaces files mid-turnaround. Checks are automatic; installing is pilot-controlled.

The GitHub digest protects against corruption and source substitution outside the configured release origin. It is **not publisher authentication**. A compromised repository can publish a new digest and asset. Protect release access, enable immutable releases where available, and sign the installer for Windows publisher identity. See [GitHub release API/digests](https://docs.github.com/en/rest/releases/releases) and [Inno signing](https://jrsoftware.org/ishelp/topic_setup_signtool.htm). Signing does not guarantee immediate SmartScreen reputation.

## Maintainer setup

1. The GitHub target is DivineStag/ADM. The owner selected the ADM freeware licence with source reuse rights reserved. Confirm runtime/media distribution rights and finish the live/clean-PC gates before public publication.
2. Protect the default branch and releases. Create a protected `community-release` environment with required human reviewers.
3. Provide a controlled Windows x64 release runner labelled `adm-release`, with .NET 10, the official developer SDK, Inno Setup 6.7+ and GitHub CLI. Do not commit proprietary SDK files or certificates. Ordinary CI can build/test without the SDK but cannot produce the live runtime package.
4. Configure signing on the runner (certificate key stays in its protected store/provider). `ADM_SIGN_COMMAND` in the protected environment passes the runner's configured signing command to Inno's SignTool. Use the Inno `$f` placeholder; do not put a private key in the repository. The draft workflow refuses an unsigned output.
5. Run the explicit release-draft workflow after review. It creates a **draft**, never publishes automatically. Review contents/signature/qualification, then publish a stable release. The latest-stable updater ignores drafts and prereleases.

Current local installer is unsigned and is only for qualification. GitHub hosting alone does not change Windows' publisher warning. Public source includes the verified Pixabay outgoing pop with separate licence notices. It retains the original incoming WAV fallback; the owner-supplied incoming MP3 remains optional and is included in local qualification packages. SDK/runtime binaries and installers remain excluded from source. Local builds retain the supplied MP3 preference. The previous script installer is retained as historical engineering evidence; it is superseded as the pilot delivery path.

GSX owns the aircraft's brake/engine and physical service-readiness checks. ADM no longer repeats those cockpit checks; it still observes service availability, approvals, sequencing and current flight/connection identity. See [readiness policy](gsx-readiness-policy.md).


GitHub writes are available and the source is published. Actions are pinned to verified official commits. Controlled release runners must use Actions Runner 2.327.1 or later. See public-release-readiness.md for current checks and remaining release decisions.
