# ClearlySaid subscription plans

ClearlySaid owns plan definitions on the server. Clients and administrators select a plan identifier; they cannot supply an arbitrary monthly allowance.

| Plan | Monthly refinements | Web monthly | Web annual | Assignment | Purpose |
| --- | ---: | ---: | ---: | --- | --- |
| Free | 20 | $0 | $0 | New accounts and administrators | Trial and occasional use |
| Development | 10,000 | Not sold | Not sold | Administrators only | Internal development and testing |
| Standard | 300 | $2.49 | $24.99 | Administrator or verified purchase | Baseline paid subscription |
| Pro | 1,000 | $4.99 | $49.99 | Administrator or verified purchase | Frequent use |

The Admin role is unlimited and always bypasses the monthly subscription quota. It still observes technical safeguards such as the 5,000-character request limit, one active refinement at a time, rate limiting, authentication, and abuse protection.

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
