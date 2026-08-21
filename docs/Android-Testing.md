# ClearlySaid Android test distribution

Web01 serves the testing package at:

`https://clearlysaid.ai/downloads/ClearlySaid-Android-Test.apk`

The package is protected by ClearlySaid authentication and the Admin role. The web application displays the Android testing download control only to a signed-in administrator. Signed-out visitors and non-admin users see an Android and iPhone coming soon banner instead. A direct unauthenticated request to the package must return `401`, and an authenticated non-admin request must return `403`.

## Build the package

On a machine with the .NET 10 SDK, .NET MAUI workload, Android SDK, and JDK installed:

```powershell
.\scripts\Publish-Android-Test.ps1
```

The script builds a signed Debug APK and places it in the Web01 static download directory. Run the script on the same secured build machine for every update so test devices receive packages signed by the same development certificate. Store releases must use a dedicated protected release keystore instead.

Publish Web01 after generating the APK:

```powershell
.\scripts\Publish-Web01.ps1
```

## Tester instructions

1. Open the ClearlySaid public website on the Android device and sign in with an Admin account.
2. Select **Download Android test app** from the administrator test-access banner.
3. Android may ask the tester to allow installation from that browser or file manager. Enable it only for this installation.
4. Open the downloaded APK and approve the installation.
5. Disable the temporary **Install unknown apps** permission afterward.

This testing APK is not a Play Store release. Android and the browser will display sideloading warnings. Distribute the link only to intended testers and replace it with Play Console internal testing before broad distribution.

The adjacent `.sha256` file contains the package checksum for integrity verification.
