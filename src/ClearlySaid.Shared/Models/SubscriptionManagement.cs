namespace ClearlySaid.Shared.Models;

public sealed class SubscriptionManagement
{
    public int Revision { get; set; }
    public int FreeTrialDays { get; set; } = 10;
    public List<SubscriptionFee> Plans { get; set; } =
    [
        new() { Id = SubscriptionPlans.Free, Name = "Free" },
        new() { Id = SubscriptionPlans.Standard, Name = "Standard", MonthlyPrice = 1.99m, AnnualPrice = 24.99m },
        new() { Id = SubscriptionPlans.Pro, Name = "Pro", MonthlyPrice = 2.49m, AnnualPrice = 49.99m }
    ];
}

public sealed class SubscriptionFee
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal MonthlyPrice { get; set; }
    public decimal AnnualPrice { get; set; }
    public bool Offered { get; set; } = true;
    public string? StripeMonthlyPriceId { get; set; }
    public string? StripeAnnualPriceId { get; set; }

    public SubscriptionPlan ToPublicPlan() => SubscriptionPlans.GetRequired(Id) with
    {
        DisplayName = Name,
        MonthlyAllowance = int.MaxValue,
        MonthlyPrice = MonthlyPrice,
        AnnualPrice = AnnualPrice,
        IsPurchasable = Id != SubscriptionPlans.Free && Offered
    };
}
