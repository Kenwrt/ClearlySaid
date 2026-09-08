using System.Text.Json;
using ClearlySaid.Shared.Models;
using Npgsql;
using NpgsqlTypes;

namespace ClearlySaid.Web.Data;

public sealed partial class ClearlySaidDatabase
{
    private async Task InitializeSubscriptionManagementAsync(CancellationToken cancellationToken)
    {
        var initial = new SubscriptionManagement();
        foreach (var plan in initial.Plans.Where(p => p.Id != SubscriptionPlans.Free))
        {
            var segment = plan.Id == SubscriptionPlans.Standard ? "Standard" : "Pro";
            plan.StripeMonthlyPriceId = configuration[$"Stripe:Prices:{segment}Monthly"];
            plan.StripeAnnualPriceId = configuration[$"Stripe:Prices:{segment}Annual"];
        }
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS clearlysaid_subscription_settings (
                id integer PRIMARY KEY CHECK (id = 1), settings jsonb NOT NULL
            );
            CREATE TABLE IF NOT EXISTS clearlysaid_subscription_settings_audit (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                actor_id uuid NOT NULL, occurred_at timestamptz NOT NULL DEFAULT now(),
                previous_settings jsonb NOT NULL, new_settings jsonb NOT NULL
            );
            CREATE TABLE IF NOT EXISTS clearlysaid_stripe_price_plans (
                price_id text PRIMARY KEY, plan_id text NOT NULL
            );
            ALTER TABLE clearlysaid_users ADD COLUMN IF NOT EXISTS free_trial_started_at timestamptz NULL;
            ALTER TABLE clearlysaid_users ADD COLUMN IF NOT EXISTS free_trial_ends_at timestamptz NULL;
            INSERT INTO clearlysaid_subscription_settings (id, settings) VALUES (1, @settings)
                ON CONFLICT DO NOTHING;
            """;
        command.Parameters.AddWithValue("settings", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(initial));
        await command.ExecuteNonQueryAsync(cancellationToken);
        // Retain the original configured IDs even after an administrator replaces them.
        await RememberPricesAsync(connection, transaction, initial, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SubscriptionManagement> GetSubscriptionManagementAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT settings::text FROM clearlysaid_subscription_settings WHERE id = 1";
        return JsonSerializer.Deserialize<SubscriptionManagement>((string)(await command.ExecuteScalarAsync(cancellationToken))!)!;
    }

    private async Task<SubscriptionPlan> GetManagedPlanAsync(string planId, CancellationToken cancellationToken)
    {
        var definition = SubscriptionPlans.GetRequired(planId);
        return definition.IsInternal ? definition :
            (await GetSubscriptionManagementAsync(cancellationToken)).Plans.Single(p => p.Id == planId).ToPublicPlan();
    }

    public async Task<SubscriptionManagement> SaveSubscriptionManagementAsync(
        Guid actorId, SubscriptionManagement settings, CancellationToken cancellationToken)
    {
        if (settings.FreeTrialDays is < 1 or > 365 || settings.Plans is null || settings.Plans.Count != 3 ||
            settings.Plans.Any(p => p is null) ||
            !settings.Plans.Select(p => p.Id).Order().SequenceEqual(new[] { "free", "pro", "standard" }) ||
            settings.Plans.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60 ||
                p.MonthlyPrice is < 0 or > 10000 || p.AnnualPrice is < 0 or > 100000 ||
                decimal.Round(p.MonthlyPrice, 2) != p.MonthlyPrice || decimal.Round(p.AnnualPrice, 2) != p.AnnualPrice ||
                !ValidPriceId(p.StripeMonthlyPriceId) || !ValidPriceId(p.StripeAnnualPriceId)) ||
            settings.Plans.Any(p => p.Id == "free" ? p.MonthlyPrice != 0 || p.AnnualPrice != 0 || !p.Offered :
                p.MonthlyPrice <= 0 || p.AnnualPrice <= 0))
            throw new ArgumentException("Enter three valid plans, positive paid prices with at most two decimals, and a trial from 1 to 365 days. Free must remain offered at $0.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT settings::text FROM clearlysaid_subscription_settings WHERE id = 1 FOR UPDATE";
        var previous = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
        var previousSettings = JsonSerializer.Deserialize<SubscriptionManagement>(previous)!;
        if (settings.Revision != previousSettings.Revision)
            throw new InvalidOperationException("Another administrator changed these settings. Reload before saving.");
        settings.Revision++;
        foreach (var plan in settings.Plans) plan.Name = plan.Name.Trim();
        await RememberPricesAsync(connection, transaction, previousSettings, cancellationToken);
        await RememberPricesAsync(connection, transaction, settings, cancellationToken);
        command.CommandText = """
            UPDATE clearlysaid_subscription_settings SET settings = @settings WHERE id = 1;
            INSERT INTO clearlysaid_subscription_settings_audit (actor_id, previous_settings, new_settings)
                VALUES (@actor, @previous, @settings);
            """;
        command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("previous", NpgsqlDbType.Jsonb, previous);
        command.Parameters.AddWithValue("settings", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(settings));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return settings;
    }

    private static bool ValidPriceId(string? value) => string.IsNullOrEmpty(value) ||
        (value.Length <= 100 && value.StartsWith("price_", StringComparison.Ordinal) && value.Skip(6).Any() &&
         value.Skip(6).All(char.IsAsciiLetterOrDigit));

    private static async Task RememberPricesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SubscriptionManagement settings, CancellationToken cancellationToken)
    {
        foreach (var plan in settings.Plans.Where(p => p.Id != SubscriptionPlans.Free))
        foreach (var price in new[] { plan.StripeMonthlyPriceId, plan.StripeAnnualPriceId }.Where(p => !string.IsNullOrEmpty(p)))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO clearlysaid_stripe_price_plans (price_id, plan_id) VALUES (@price, @plan)
                    ON CONFLICT DO NOTHING;
                SELECT plan_id FROM clearlysaid_stripe_price_plans WHERE price_id = @price;
                """;
            command.Parameters.AddWithValue("price", price!);
            command.Parameters.AddWithValue("plan", plan.Id);
            if ((string)(await command.ExecuteScalarAsync(cancellationToken))! != plan.Id)
                throw new ArgumentException("A Stripe price already belongs to another plan. Use a different price ID.");
        }
    }

    public async Task<SubscriptionPlan?> FindStripePricePlanAsync(string priceId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT plan_id FROM clearlysaid_stripe_price_plans WHERE price_id = @price";
        command.Parameters.AddWithValue("price", priceId);
        return SubscriptionPlans.Find(await command.ExecuteScalarAsync(cancellationToken) as string);
    }
}
