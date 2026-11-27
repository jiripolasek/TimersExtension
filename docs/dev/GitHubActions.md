# GitHub Actions and releases

[Development guide](README.md)

## Build and tag

1. Set the intended `MAJOR.MINOR.PATCH.0` version in the [package manifest](../../src/JPSoftworks.TimersExtension.Package/Package.appxmanifest). Add the matching `## [MAJOR.MINOR.PATCH]` section to the [changelog](../../CHANGELOG.md).
2. Run [CI](../../.github/workflows/ci.yml) on the release commit. CI builds x64 and ARM64 Native AOT Store upload packages and runs the managed tests on x64.
3. In **Actions → Tag release**, select the release branch and run the workflow. It creates `vMAJOR.MINOR.PATCH` from the manifest version and starts [Release](../../.github/workflows/release.yml). For an existing tag, dispatch **Release** on that tag.

## Store and GitHub release

1. Download the x64 and ARM64 `unsigned-store-packages-*` artifacts from the Release run and extract their `.msixupload` files.
2. Upload both files to the product’s **Packages** section in Partner Center and submit for certification. [Microsoft Store package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/package-version-numbering?pivots=store-installer-msix)
3. Before publishing the GitHub draft for direct downloads, attach the final Store-signed x64 and ARM64 `.msix` installers. The [WinGet validator](../../eng/Get-WinGetRelease.ps1) checks the version, identity, architectures, and Store signatures. Smoke-test the signed installers before making them available.

For local Store upload builds, use [`Get-MsixIdentity.ps1`](../../eng/Get-MsixIdentity.ps1) to check the embedded package identity. Visual Studio can reuse a staged MSIX when the identity changes at the same version.

Once the Store listing is live, users can install through the Microsoft Store source in WinGet.

```powershell
winget install --id 9P32P0T1C3VX --source msstore
```

## First WinGet community release

The first published GitHub release can trigger an update before the community package exists. Use the steps below to submit the first version.

1. After the GitHub release is public, copy the two signed `.msix` asset URLs. Install [Windows Package Manager Manifest Creator](https://github.com/microsoft/winget-create) and pass both URLs to `wingetcreate new`.

   ```powershell
   winget install --id Microsoft.WingetCreate --exact --source winget
   wingetcreate new '<x64 MSIX asset URL>' '<ARM64 MSIX asset URL>'
   ```

2. Use `JiriPolasek.TimersforCommandPalette` as the package identifier and the MSIX version as the package version. For release `v0.0.1`, use `0.0.1.0`. Check that the manifest lists both architectures and submit it to `microsoft/winget-pkgs`. [Manifest creation](https://github.com/microsoft/winget-create/blob/main/doc/new.md)
3. After the submission is merged and indexed, confirm it with `winget show --id JiriPolasek.TimersforCommandPalette --exact --source winget`.

## WinGet updates

Fork `microsoft/winget-pkgs` under the Timers repository owner. Create a classic GitHub personal access token with the `public_repo` scope, then save it as the `WINGET_TOKEN` repository secret under **Settings → Secrets and variables → Actions**. [WinGet Releaser setup](https://github.com/vedantmgoyal9/winget-releaser#getting-started)

[Publish to WinGet](../../.github/workflows/winget.yml) updates an existing community manifest on a published stable GitHub release. Once the first manifest is available, the next release can use the automatic run. To retry a later submission, dispatch the workflow with its `release_tag`.
