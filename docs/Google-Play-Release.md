# ClearlySaid Google Play release checklist

## One-time decisions

- Confirm the permanent Android application ID before the first upload. It is currently `com.clearlysaid.app`.
- Create or verify the Google Play organization developer account.
- Create ClearlySaid as a free download with in-app subscriptions.
- Enable Google Play App Signing and retain a protected backup of the upload key.

## Product catalog

Create these subscription identifiers after final pricing approval:

- `clearlysaid_personal_monthly` — proposed 300 refinements per period.
- `clearlysaid_personal_annual` — proposed 300 refinements per month while active.
- `clearlysaid_pro_monthly` — proposed 1,000 refinements per period.
- `clearlysaid_pro_annual` — proposed 1,000 refinements per month while active.

Never grant an entitlement from unverified data supplied by the Android app.

## Build the upload bundle

Install .NET 10, the MAUI Android workload, Android SDK/API 36, and a compatible JDK on WrightServer. Set the protected signing variables only in the current terminal or secret manager:

```powershell
$env:CLEARLYSAID_ANDROID_KEYSTORE = "D:\Secure\ClearlySaid-upload.keystore"
$env:CLEARLYSAID_ANDROID_KEY_ALIAS = "clearlysaid-upload"
$env:CLEARLYSAID_ANDROID_STORE_PASSWORD = "..."
$env:CLEARLYSAID_ANDROID_KEY_PASSWORD = "..."
.\scripts\Publish-Android-Release.ps1
```

Upload the generated `.aab`, never the debug testing APK. Increment `ApplicationVersion` for every Play upload.

## Store and policy material

- Publish `/privacy` and `/account/delete` from the public ClearlySaid site.
- Verify that `support@healthcareautomation.services` is a working monitored address.
- Complete Data Safety, content rating, target audience, ads, app-access, microphone, and subscription declarations.
- Provide 512×512 icon, 1024×500 feature graphic, phone screenshots, and store descriptions.

## Test and release

1. Upload to Internal testing and verify registration, login, dictation, quota enforcement, purchase restoration, failures, and account deletion.
2. Run the required Closed test. New personal accounts generally require 12 continuously opted-in testers for 14 days before requesting production access.
3. Review the pre-launch report and Android vitals.
4. Roll out production at 10%, 25%, 50%, then 100% while watching errors, database health, API usage, and OpenAI cost.

Google Play Billing is intentionally not activated until the Play Console application, products, service account, and package name exist. The server endpoint returns 503 rather than trusting an unverifiable purchase.
