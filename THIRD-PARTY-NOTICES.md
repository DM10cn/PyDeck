# 📚 Third-party notices

**English** · [简体中文](docs/THIRD-PARTY-NOTICES.zh-CN.md) · [🏠 Home](README.md)

## 🐍 Python names and artwork

PyDeck is an independent companion for Python Install Manager. It is not affiliated with or endorsed by the Python Software Foundation.

Python and the Python logos are trademarks or registered trademarks of the Python Software Foundation. The application icon incorporates Python-inspired logo artwork. The project's MIT license does not grant rights to third-party trademarks or imply endorsement. Consult the [PSF trademark policy](https://www.python.org/psf/trademarks/) when reusing or changing this artwork.

## 🪟 Platform and dependencies

PyDeck is not affiliated with or endorsed by Microsoft. Windows, .NET, WinUI, and related product names belong to their respective owners.

The project restores Microsoft .NET / Windows App SDK components through NuGet. Dependencies are listed in the project files and `packages.lock.json`; each dependency retains its own license and notices. Restored packages and runtime binaries are not vendored in this source repository.

The native prerequisite launcher is built with Microsoft C++ and statically links its release support libraries. Those components retain Microsoft's applicable terms; the launcher does not require a separately installed Visual C++ Redistributable. The main WinUI app's runtime requirements are unchanged.

Python Install Manager, Python distributions, and offline bundles are separate software with their own licenses. PyDeck's MIT license does not replace those terms. When distributing compiled builds, review the licenses and notice requirements of all included components.

📦 Release installers include a `ThirdPartyNotices` directory with available notices from restored runtime-facing packages, the Windows SDK, and the .NET host toolchain. These original license documents are retained in their original language.
