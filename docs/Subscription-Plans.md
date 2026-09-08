# ClearlySaid subscription plans

ClearlySaid owns plan definitions on the server. Administrators manage fixed fees, names, offered status, Stripe price mappings, and free-trial duration at `/admin/subscriptions`, linked from Admin Tools. Fees are not calculated from refinement counts, and no plan has a refinement-count limit. Ordinary clients cannot set entitlements. The persisted catalog is authoritative; the values below are initial defaults.

| Plan | Refinement limit | Web monthly | Web annual | Assignment | Purpose |
| --- | ---: | ---: | ---: | --- | --- |
| Free | None during trial | $0 | $0 | New accounts and administrators | 10-day trial |
| Development | None | Not sold | Not sold | Administrators only | Internal development and testing |
| Standard | None | $1.99 | $24.99 | Administrator or verified purchase | Standard subscription |
| Pro | None | $2.49 | $49.99 | Administrator or verified purchase | Pro subscription |

## Subscription management and trials

The free trial starts on the first authenticated account access after this feature is installed. This gives existing accounts a full trial rather than backdating it to registration. Its start and end are stored once per account. Updating the trial duration affects trials that have not started; it does not restart or shorten existing trials. Trial expiry is enforced on the server independently of refinement counts. After expiry, the user can still sign in, manage their account, and buy a plan. There is no automatic charge or automatic enrollment into Standard.

All refinement-count caps have been removed, including Free, Standard, Pro, and Development. Usage events remain for diagnostics, activity reporting, and duplicate-request prevention, but are not counted to permit or deny requests. Legacy allowance fields remain for compatibility and are not enforced. Annual prices are intentionally unchanged and are separately editable; their current defaults exceed twelve payments at the new monthly rates. The management page flags that condition.

Saving requires current Administrator authorization and records the actor, UTC timestamp, and previous/new settings in an append-oriented audit table within the same database transaction. A revision check prevents overwriting another administrator's edits. Existing subscriptions are not canceled when a plan is no longer offered. The internal Development plan and Administrator role are retained.

The schema additions are in `ClearlySaidDatabase.Subscriptions.cs` and run with the application's existing initialization mechanism. Editing this source does not apply them to a running/shared database; deploying this feature requires the normal explicit database/deployment authorization.

## Updating payment providers

Saving the application catalog does not mutate Stripe or Google Play. Stripe price IDs are seeded from the existing protected configuration only when the catalog is first created. Use new Stripe recurring prices for changed amounts and save the corresponding IDs in Admin Tools. Checkout retrieves the selected Stripe price and rejects inactive prices or mismatches in USD amount, interval, interval count, or billing scheme. Taxes and promotions can still affect the final checkout total. Prior Stripe price mappings are retained so existing subscribers' webhooks continue to resolve to the correct plan. No existing subscription is migrated by saving settings.

For Google Play, edit the relevant base-plan prices separately in Play Console. Existing subscribers retain legacy pricing unless explicitly migrated. Android reads the application catalog for names and availability, and the Google Play purchase sheet displays the actual local price. A new Android release is needed to distribute this catalog/trial UI change; subsequent fee-only changes to existing Play products normally do not require another binary. The application free trial is separate from a Google Play subscription offer and never automatically starts a paid subscription.

Application deployment does not update payment-provider prices or publish a Google Play release. Those operations are tracked separately.

All plans retain technical safeguards: the 5,000-character request limit, one active refinement at a time, rate limiting, authentication, and abuse protection. Administrator access continues to bypass trial expiry. Paid styling permissions remain unchanged.

## Paid message styling

Standard, Pro, Development, and Admin accounts can choose a message purpose, tone, and directness level before refinement. Free accounts do not receive these controls, and Web01 rejects forged style requests from free accounts with HTTP `403`.

Clients send only fixed option IDs. Web01 and API01 both validate those IDs, and API01 translates them into controlled model instructions for Ollama or the OpenAI fallback. Arbitrary user-supplied prompt instructions are not accepted as style parameters.

## Google Play products

- `clearlysaid_standard_monthly`
- `clearlysaid_standard_annual`
- `clearlysaid_pro_monthly`
- `clearlysaid_pro_annual`

`GET /api/subscriptions/plans` publishes the customer-visible catalog. Development is intentionally omitted. `POST /api/billing/google/verify` accepts only the configured package and product identifiers, but continues to return `503` until the Play service-account integration is connected. That fail-closed behavior prevents an Android client from granting its own entitlement.

Store pricing and trial periods belong in Google Play Console rather than application code. Once the Play Console application, products, and service account are ready, the verification endpoint should validate the purchase token with Google, persist the provider reference and renewal period, and then assign the matching Standard or Pro plan.

## Stripe web subscriptions

Stripe checkout is owned by the public web application. A successful, signature-verified Stripe webhook stores the paid entitlement against the ClearlySaid account, so the same plan is visible after the user signs into the web, Android, or iOS app with that account.

The MAUI app contains a website checkout handoff, but it is disabled by default. It may be enabled only for a distribution channel and region where external purchase links are permitted and all required store-program enrollment, disclosures, APIs, reporting, and fees have been completed. Build an approved external-link variant with:

```powershell
-p:ClearlySaidExternalPurchaseLinksEnabled=true
```

The normal Google Play build must leave that property false unless ClearlySaid has been accepted into Google Play's applicable external-links program. With the property false, users can compare the plans but purchase buttons remain disabled.

Web01 creates Stripe-hosted Checkout and Customer Portal sessions. Stripe webhooks are signature-verified, processed idempotently, and persisted as provider subscription sources. The effective ClearlySaid entitlement is selected server-side; clients cannot grant or change their own plan.

Required protected Web01 settings:

```text
Stripe__SecretKey=sk_live_...
Stripe__WebhookSecret=whsec_...
Stripe__Prices__StandardMonthly=price_...
Stripe__Prices__StandardAnnual=price_...
Stripe__Prices__ProMonthly=price_...
Stripe__Prices__ProAnnual=price_...
```

Optional settings:

```text
Stripe__PortalConfigurationId=bpc_...
Stripe__AutomaticTaxEnabled=false
```

The webhook destination is:

```text
https://clearlysaid.ai/api/billing/stripe/webhook
```

Subscribe it to `checkout.session.completed`, `customer.subscription.created`, `customer.subscription.updated`, and `customer.subscription.deleted`.
