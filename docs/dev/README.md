# Development guide

Build and test Timers for Command Palette from the repository root in PowerShell 7.

## Requirements

- .NET 11 SDK.
- Visual Studio 2026 with Windows Application Packaging support and the Windows SDK tools. Install the C++ build tools for Native AOT builds.
- PowerToys with Command Palette enabled to try the extension.

## Repository layout

| Path | Contents |
| --- | --- |
| [`src/JPSoftworks.TimersExtension`](../../src/JPSoftworks.TimersExtension) | Commands, pages, timers, stopwatches, and runtime assets |
| [`src/JPSoftworks.TimersExtension.Package`](../../src/JPSoftworks.TimersExtension.Package) | Windows Application Packaging project, manifest, and package assets |
| [`tests/JPSoftworks.TimersExtension.Tests`](../../tests/JPSoftworks.TimersExtension.Tests) | Extension tests |
| [`eng`](../../eng) | Build, deployment, and release scripts |

## Build and test

```powershell
.\Test.ps1
.\Build.ps1 -Configuration Release -Platform x64 -Aot
.\Build.ps1 -Configuration Release -Platform ARM64 -Aot
```

`Test.ps1` defaults to the host architecture. Running the ARM64 tests requires Windows on ARM64. `Build.ps1` returns an unsigned MSIX in `artifacts` and supports cross-building ARM64 on x64.

For changes to the packaging or release scripts, run the checks used by [CI](../../.github/workflows/ci.yml). Shared project settings are in [`eng/CmdPal.Extension.props`](../../eng/CmdPal.Extension.props), and script defaults are in [`eng/Package.config.psd1`](../../eng/Package.config.psd1).

## Localization

- Add a `Strings.<culture>.resx` beside [`Strings.resx`](../../src/JPSoftworks.TimersExtension/Resources/Strings.resx) for extension commands, pages, settings, and notifications. Keep resource keys and format placeholders. Missing entries fall back to English. Run the Visual Studio custom tool on the neutral file when adding or removing resource keys.
- Add `Strings/<culture>/Resources.resw` in the package project for the display name and description Windows and Command Palette read from the manifest. Match the provider name to `Provider.DisplayName` in the extension resources. See the [English package resources](../../src/JPSoftworks.TimersExtension.Package/Strings/en-us/Resources.resw).
- Translate the timer and stopwatch query prefixes, duration unit aliases, and end-time pattern with the corresponding input examples. Keep `h`, `m`, and `s` for hours, minutes, and seconds. Check that the examples parse with matching and different display languages and regional formats, then check the package metadata, lists, forms, Dock, and alarm notifications in the built MSIX.

## Local deployment

Enable [Developer Mode](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/security#developer-mode), then deploy and request a Command Palette reload.

```powershell
.\eng\Test-Package.ps1 -AfterDeploy Reload
```

Pass `-Aot` to test the Native AOT build. The script registers an unpacked development package and preserves app data when replacing a matching development registration. Use a separate test VM if the Store build is installed. The [Hyper-V deployment script](../../eng/Deploy-HyperVPackage.ps1) has usage examples and an [example VM configuration](../../eng/HyperV.config.example.psd1).

## Release

See the [GitHub Actions runbook](GitHubActions.md) for tag creation, Store submission, and WinGet publishing.
