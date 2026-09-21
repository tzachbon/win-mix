# Attribution

Win Mix's original source and documentation use the root MIT [LICENSE](../LICENSE), copyright 2026 tzachbon. The Git history at the readiness review records tzachbon as the sole author. No code or artwork was copied from the comparison projects in the readiness research.

The app icon was generated for this project. Its prompt and conversion history are in `Assets/icon-prompt.txt` in the source repository. `docs/images/mixer.png` is a capture of Win Mix 1.0.1 on Windows 11. System glyphs and other applications' icons are loaded from Windows at runtime, not checked in as project artwork. Third-party components retain their own terms.

The self-contained build carries these unmodified upstream license and notice files:

| Component | Source | Files here |
| --- | --- | --- |
| .NET runtime | Existing runtime distribution notices | `dotnet-LICENSE.txt`, `dotnet-NOTICES.txt` |
| NAudio.Core / NAudio.Wasapi 2.2.1 | Existing NAudio MIT license | `NAudio-license.txt` |
| Windows App SDK 2.5.1 and component packages | Locked NuGet packages | `WindowsAppSDK-license.txt`, `WindowsAppSDK-NOTICE.txt` |
| Windows App SDK ML 2.1.94 | NuGet package `license.txt` | `WindowsAppSDK-ML-license.txt` |
| WinUI 2.3.9 | NuGet package `NOTICE.txt` | `WinUI-NOTICE.txt` |
| WebView2 1.0.3719.77 | NuGet package `LICENSE.txt` and `NOTICE.txt` | `WebView2-LICENSE.txt`, `WebView2-NOTICE.txt` |
| Windows ML 2.1.74, including ONNX Runtime | NuGet package `license.txt` and `ThirdPartyNotices.txt` | `WindowsML-license.txt`, `WindowsML-NOTICES.txt` |
| System.Numerics.Tensors 9.0.0 | NuGet package `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` | `Tensors-LICENSE.txt`, `Tensors-NOTICES.txt` |

The Windows App SDK component packages share the main license text except ML. WinUI has an additional notice file. SDK BuildTools and Inno Setup are build tools, not application payload. Versions come from `packages.lock.json` in the source repository. Recheck this inventory against the published files whenever dependencies change. The project's MIT license does not replace Microsoft's binary distribution terms or any third-party notices.
