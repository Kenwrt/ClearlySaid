# Text Messaging Preparation

ClearlySaid can now collect an optional US or Canadian mobile number and record separate consent for future account and service text messages. No outbound SMS provider is installed or enabled.

## Current safeguards

- Saving a phone number does not grant consent.
- Consent is limited to account and service messages. It does not include marketing.
- Consent wording, version, source, timestamp, number, and status are recorded in an append-only audit table.
- A phone number is stored in E.164 format and remains unverified until a future provider completes an OTP verification flow.
- Changing or removing a phone number withdraws consent associated with the prior number.
- Account deletion removes the current phone and scrubs phone numbers from consent audit events.
- Diagnostic logs must never contain full phone numbers, message bodies, verification codes, or provider credentials.

## Future provider integration checklist

1. Add a provider-specific NuGet package only to the server project. Do not place provider credentials in the shared UI or MAUI client.
2. Implement `ISmsMessageSender` and register it only when messaging is explicitly enabled and required configuration is present.
3. Add an OTP verification flow. Store only a one-way hash of the short-lived code, enforce expiration and attempt limits, then set `phone_verified_at` after successful verification.
4. Before every send, require an active account, a verified number, the correct consent scope, and a status that permits sending.
5. Process provider webhooks idempotently. Validate provider signatures before accepting delivery receipts or inbound commands.
6. Treat STOP, END, CANCEL, UNSUBSCRIBE, and QUIT as immediate opt-out commands. Suppress future sends before returning success to the provider. Support HELP with accurate support information.
7. Record provider message ID, category, timestamps, delivery state, and failure code. Do not record message content in diagnostic logs.
8. Apply rate limits, quiet-hour policy where required, retry limits, and an idempotency key to prevent duplicate sends.
9. Configure sender registration, campaign registration, and carrier requirements with the selected provider before production use.
10. Complete counsel review of the actual message program, consent copy, Terms, Privacy Policy, retention policy, and federal and state requirements before enabling outbound messaging.

## Reference guidance

- [CTIA Messaging Principles and Best Practices](https://www.ctia.org/the-wireless-industry/industry-commitments/messaging-interoperability-sms-mms)
- [FCC consent revocation order](https://docs.fcc.gov/public/attachments/FCC-24-24A1.pdf)
- [FCC April 2025 compliance notice](https://docs.fcc.gov/public/attachments/DA-25-312A1.pdf)

This preparation is an engineering control set, not a legal determination or a guarantee of compliance.
