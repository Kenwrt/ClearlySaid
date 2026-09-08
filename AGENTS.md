# ClearlySaid project guidance

- If explicitly requested, build with `dotnet build ClearlySaid.sln --disable-build-servers -p:UseSharedCompilation=false`. Do not verify Web, API, Shared, or MAUI components unless explicitly requested.
- Preserve billing, accounts, analytics, provider routing, consent, and testing behavior unless explicitly changed.
- Keep local-first Ollama routing privacy-safe. Do not log prompts, outputs, message bodies, phone numbers, verification codes, image bytes, filenames, or provider response bodies.
- A complete requested release must report Web, API, Android artifact/signing, Git push, server deployment, health, and mobile-store status separately.
- API01, Web01, production containers, Android signing, Google Play, credentials, and database changes must be explicitly included in the user's request. When included, proceed without asking again.
