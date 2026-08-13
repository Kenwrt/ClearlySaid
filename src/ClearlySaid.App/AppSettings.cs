namespace ClearlySaid.App;

public static class AppSettings
{
    public const string ServerBaseUrl = "https://clearlysaid.ai/";
    public const string SubscriptionWebsiteUrl =
        "https://clearlysaid.ai/?upgrade=1";

#if CLEARLYSAID_EXTERNAL_PURCHASE_LINKS
    public const bool ExternalPurchaseLinksEnabled = true;
#else
    public const bool ExternalPurchaseLinksEnabled = false;
#endif
}
