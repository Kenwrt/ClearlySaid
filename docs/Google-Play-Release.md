# ClearlySaid Google Play release checklist

## One-time decisions

- Confirm the permanent Android application ID before the first upload. It is currently `com.clearlysaid.app`.
- Create or verify the Google Play organization developer account.
- Create ClearlySaid as a free download with in-app subscriptions.
- Enable Google Play App Signing and retain a protected backup of the upload key.

## Product catalog

Create two subscriptions, each with `monthly` and `annual` auto-renewing base plans:

- `clearlysaid_standard` — Standard, 300 refinements per month. Price `monthly` at $2.49/month and `annual` at $24.99/year.
- `clearlysaid_pro` — Pro, 1,000 refinements per month. Price `monthly` at $4.99/month and `annual` at $49.99/year.

Activate both base plans and make them available in the same countries as the app. Product and base-plan IDs are permanent after activation.

Free includes 20 refinements per month. Development includes 10,000 and can only be assigned by an administrator. Administrators bypass the monthly quota regardless of their selected plan. Never grant an entitlement from unverified data supplied by the Android app.

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

The Android app uses Google Play Billing Library 9.1, server-side verification through the Android Publisher API, server-side acknowledgement, account-bound purchase tokens, and automatic purchase restoration. Web01 never grants an entitlement from data supplied by the Android app alone.

## Google Play service account

1. In Play Console, finish the Google merchant payments profile first.
2. Create a Google Cloud service account dedicated to ClearlySaid and enable the Google Play Android Developer API.
3. Grant it only the Play Console permissions needed to view orders/subscriptions and manage subscription purchases.
4. Store its JSON credential on Web01 at `/home/ken/clearlysaid/secrets/google-play-service-account.json` with mode `600`.
5. Mount the file read-only into the web container. Set `GooglePlay__ServiceAccountJsonPath` to its container path and set `GooglePlay__PackageName=com.clearlysaid.app`.

Never commit the JSON credential or include it in the Android app.

## Website purchase links

Google Play's Payments policy generally prohibits a Play-distributed app from directing users to a different payment method for digital subscriptions. Eligible developers may use external links only after enrolling in the applicable Google Play program and completing its technical, disclosure, reporting, and fee requirements.

ClearlySaid therefore defaults `ClearlySaidExternalPurchaseLinksEnabled` to `false`. The app still shows the Free, Standard, and Pro plan comparison, but its purchase buttons say that purchases are coming soon. Do not enable the external website buttons in a Play upload until enrollment has been approved for every country included in that release.

After approval, build the eligible release with:

```powershell
./scripts/Publish-Android-Release.ps1 -ExternalLinksProgramApproved
```

Website purchases must use the same ClearlySaid login. Stripe's verified webhook—not the mobile app or return URL—assigns the entitlement, and the user can select **Refresh plan** in the app afterward.

Official policy references:

- https://support.google.com/googleplay/android-developer/answer/10281818
- https://support.google.com/googleplay/android-developer/answer/16470497
