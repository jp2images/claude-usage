# Code Signing & EDR — Study Notes

A recap of the signing/blocking discussion that drove this repo's move to native
per-platform apps. Captured for later study.

---

## 1. The core problem

The Go/Fyne binary is **blocked on a corporate Windows machine running
CrowdStrike**. The surface error was misleading ("the architecture isn't correct
for this machine," even though it was built locally). That generic/garbled error
is a known way a CrowdStrike (Falcon) **process block** surfaces — the OS reports
something odd rather than "blocked by security."

Only the Go app was blocked; normal Visual Studio output runs fine.

## 2. Why an EDR singles out an unsigned Go binary

A freshly built, unsigned Go executable hits several heuristics at once:

- **Unsigned** — no Authenticode publisher signature → zero reputation.
- **Statically linked & large** — Go bundles its whole runtime into one ~36 MB
  self-contained file. That pattern resembles packed/standalone malware to ML
  detection.
- **Go is over-represented in malware** — a lot of commodity malware and
  red-team tooling is written in Go, so vendors weight unsigned Go binaries as
  suspicious by default.

A framework-dependent **.NET** app built with the standard toolchain reads as a
normal managed app (known compiler, dynamically linked against the shared
runtime), so it largely avoids this class of false positive. This is a major
reason the project moved to native apps (.NET/Avalonia on Windows, SwiftUI on
macOS).

## 3. What blocks an app, and the right fix for each

| Mechanism | Looks like | Right fix |
|-----------|-----------|-----------|
| **SmartScreen** | Blue "Windows protected your PC" | Tied to the "mark of the web" on *downloaded* files. A **locally compiled** binary has no MOTW and usually runs. Reputation builds with signing + downloads over time. |
| **AppLocker / WDAC** | "Blocked by your administrator" | Allowlist by **publisher certificate** (preferred) or path/hash. Local build does NOT help. |
| **EDR (CrowdStrike, etc.)** | Generic/garbled error, silent kill | Centrally managed; you can't override locally. Needs an **exception in the Falcon console** (by certificate, ideally) and/or a trusted signature. |

## 4. Authenticode signing (Windows)

- **What it is**: an embedded signature binding the exe to a publisher via a
  code-signing certificate, plus a timestamp so it stays valid after the cert
  expires.
- **Certificate options**:
  - **Internal CA cert** (best for corp): many enterprises run their own PKI and
    can issue a code-signing cert already trusted org-wide. Ask IT.
  - **Commercial OV/EV cert**: trusted publicly; EV usually requires a hardware
    token. Overkill for a personal tool.
  - **Self-signed**: only helps if IT adds it to trusted publishers / as a CS
    exception.
- **Command** (`signtool` ships with the Windows SDK):
  ```powershell
  signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 ClaudeUsage.exe
  ```
- **Cross-platform alternative**: `osslsigncode` can Authenticode-sign a Windows
  exe from macOS/Linux.

### Critical detail for an app you iterate on
The binary's hash changes on every rebuild, so **hash-based allowlisting is
useless** for active development. Sign every build with the **same certificate**
and ask IT to allowlist the **certificate/publisher once** — then every future
rebuild stays trusted. Automate signing in the build/release pipeline so you
never ship an unsigned artifact. A signature lowers heuristic suspicion but does
**not** override an explicit EDR policy — only an exception does that.

## 5. macOS signing (for completeness)

The original macOS app is **ad-hoc signed** (`Signature=adhoc`,
`TeamIdentifier=not set`). That runs fine on the build machine but isn't trusted
elsewhere. Identities seen locally:

- **Apple Development** — run on your own registered dev machines.
- **Apple Distribution** — App Store / TestFlight.
- **Developer ID Application** *(was missing)* — the one needed to distribute a
  `.app` that runs on *any* Mac outside the App Store; also the one that supports
  **notarization**.

The robust macOS distribution path: sign with **Developer ID Application** +
**hardened runtime** → **notarize** (`xcrun notarytool`) → **staple** the ticket.
That's what lets a `.app` run anywhere without Gatekeeper complaints. Building
locally avoids the quarantine attribute, which is why dev builds "just work" on
the build machine.

## 6. Takeaways

1. The CrowdStrike block is an **EDR policy/heuristic** issue, not a CPU
   architecture issue.
2. Unsigned static Go binaries are a near-worst-case for EDR heuristics; native
   managed/Swift apps avoid that.
3. On a managed machine you **cannot** self-bypass the EDR — the sanctioned route
   is IT signing + a **certificate-based** exception.
4. For an actively developed tool, always prefer **publisher/certificate**
   allowlisting over per-hash, and automate signing.
