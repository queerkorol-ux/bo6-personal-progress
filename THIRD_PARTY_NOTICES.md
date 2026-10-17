# Third-party material and license scope

The MIT license covers the original authored bridge, launcher, tooling and
documentation. It does **not** license Call of Duty, its executable code,
assets, reverse-engineered game-derived bytes or trademarks. Their respective
owners retain their rights. This project is unaffiliated with the game publisher,
NVIDIA, Microsoft and any offline installation provider.

## Game-derived compatibility data retained in the runtime

The unchanged, build-specific runtime contains original instruction bytes,
layout/schema constants, hashes and guard patterns. In particular,
`BO6_Personal_Progress/source/CaptionFixture.cs` embeds a compressed 2,568-byte
checkpoint-notification chunk, its 4,653-byte original and modified bytecode
images, and the project's 385-byte authored patch handler. These embedded
**game-derived** bytecode images and instruction fragments are not covered by
our MIT grant. They are retained because the exact comparisons and isolated
preflight are required by this existing runtime; the license makes no claim
that they are original project assets or grants permission on behalf of their
owners. Review applicable rights before redistributing them.

The repository does not include a game executable, game DLL, driver,
authentication component, offline emulator, extracted standalone Lua file,
full memory capture or personal save. A compatible existing installation is
required. No third-party game binary is downloaded by the build scripts.

## Build and platform dependencies

Windows PowerShell, the .NET Framework and the Windows compiler are external
Microsoft components, not vendored here. GitHub's checkout action is referenced
by commit in CI and remains subject to its own license. No third-party library
is bundled as a runtime dependency of the authored launcher.
