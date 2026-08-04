using System.Security.Cryptography;
using System.Text;
using ClearlySaid.Shared.Models;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using NpgsqlTypes;

namespace ClearlySaid.Web.Data;

public sealed class ClearlySaidDatabase(
    IConfiguration configuration,
    IPasswordHasher<UserCredential> passwordHasher,
    ILogger<ClearlySaidDatabase> logger)
{
    private const int FreeMonthlyAllowance = 20;
    private readonly string connectionString = configuration.GetConnectionString("ClearlySaid")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:ClearlySaid is required. Configure it with an environment variable; never commit a database password.");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
        var bootstrapEmail = configuration["Admin:BootstrapEmail"];
        if (!string.IsNullOrWhiteSpace(bootstrapEmail))
        {
            await using var bootstrapCommand = connection.CreateCommand();
            bootstrapCommand.CommandText = """
                UPDATE clearlysaid_users SET role = 'Admin'
                WHERE normalized_email = @email AND disabled_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM clearlysaid_users
                      WHERE role = 'Admin' AND disabled_at IS NULL
                  );
                """;
            bootstrapCommand.Parameters.AddWithValue("email", NormalizeEmail(bootstrapEmail));
            var updated = await bootstrapCommand.ExecuteNonQueryAsync(cancellationToken);
            if (updated == 0)
            {
                logger.LogWarning("The configured admin bootstrap account does not exist yet.");
            }
        }
        logger.LogInformation("ClearlySaid PostgreSQL schema is ready.");
    }

    public async Task<AuthResponse?> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = new UserCredential(Guid.NewGuid(), normalizedEmail);
        var passwordHash = passwordHasher.HashPassword(user, password);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO clearlysaid_users (id, email, normalized_email, password_hash)
                    VALUES (@id, @email, @normalizedEmail, @passwordHash);
                    INSERT INTO clearlysaid_entitlements
                        (user_id, plan_id, monthly_allowance, status, period_started_at, period_ends_at)
                    VALUES (@id, 'free', @allowance, 'active', date_trunc('month', now()), date_trunc('month', now()) + interval '1 month');
                    """;
                command.Parameters.AddWithValue("id", user.Id);
                command.Parameters.AddWithValue("email", email.Trim());
                command.Parameters.AddWithValue("normalizedEmail", normalizedEmail);
                command.Parameters.AddWithValue("passwordHash", passwordHash);
                command.Parameters.AddWithValue("allowance", FreeMonthlyAllowance);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var session = await CreateSessionAsync(connection, transaction, user.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var account = await GetAccountAsync(user.Id, cancellationToken);
            return new AuthResponse(session.Token, session.ExpiresAt, account);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
    }

    public async Task<AuthResponse?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, normalized_email, password_hash
            FROM clearlysaid_users
            WHERE normalized_email = @email AND disabled_at IS NULL;
            """;
        command.Parameters.AddWithValue("email", NormalizeEmail(email));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var user = new UserCredential(reader.GetGuid(0), reader.GetString(1));
        var passwordHash = reader.GetString(2);
        var verification = passwordHasher.VerifyHashedPassword(user, passwordHash, password);
        await reader.CloseAsync();
        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var session = await CreateSessionAsync(connection, transaction, user.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var account = await GetAccountAsync(user.Id, cancellationToken);
        return new AuthResponse(session.Token, session.ExpiresAt, account);
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(
        string? accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id, u.email, u.role
            FROM clearlysaid_access_tokens t
            JOIN clearlysaid_users u ON u.id = t.user_id
            WHERE t.token_hash = @hash
              AND t.revoked_at IS NULL
              AND t.expires_at > now()
              AND u.disabled_at IS NULL;
            """;
        command.Parameters.AddWithValue("hash", HashToken(accessToken));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AuthenticatedUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    public async Task<AccountInfo> GetAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        await EnsureCurrentPeriodAsync(userId, cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = AccountSql;
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The account entitlement could not be found.");
        }

        return ReadAccount(reader);
    }

    public async Task<UsageReservation> TryReserveUsageAsync(
        Guid userId,
        Guid requestId,
        int characterCount,
        CancellationToken cancellationToken)
    {
        await EnsureCurrentPeriodAsync(userId, cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH entitlement AS (
                SELECT user_id, monthly_allowance, period_started_at, period_ends_at
                FROM clearlysaid_entitlements
                WHERE user_id = @userId AND status = 'active'
                FOR UPDATE
            ), usage_count AS (
                SELECT count(*)::int AS used
                FROM clearlysaid_usage_events e, entitlement q
                WHERE e.user_id = q.user_id
                  AND e.occurred_at >= q.period_started_at
                  AND e.occurred_at < q.period_ends_at
                  AND e.status IN ('reserved', 'completed')
            )
            INSERT INTO clearlysaid_usage_events
                (user_id, request_id, character_count, estimated_input_tokens, status, succeeded)
            SELECT entitlement.user_id, @requestId, @characterCount,
                   ceiling(@characterCount / 4.0)::integer, 'reserved', false
            FROM entitlement, usage_count
            WHERE usage_count.used < entitlement.monthly_allowance
            ON CONFLICT DO NOTHING
            RETURNING id;
            """;
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("requestId", requestId);
        command.Parameters.AddWithValue("characterCount", characterCount);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var duplicate = false;
        if (result is not long)
        {
            await using var duplicateCommand = connection.CreateCommand();
            duplicateCommand.Transaction = transaction;
            duplicateCommand.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM clearlysaid_usage_events
                    WHERE user_id = @userId AND request_id = @requestId
                );
                """;
            duplicateCommand.Parameters.AddWithValue("userId", userId);
            duplicateCommand.Parameters.AddWithValue("requestId", requestId);
            duplicate = (bool)(await duplicateCommand.ExecuteScalarAsync(cancellationToken) ?? false);
        }

        await transaction.CommitAsync(cancellationToken);
        return new UsageReservation(result as long?, duplicate);
    }

    public async Task CompleteUsageAsync(
        long usageId,
        RefineMessageResponse result,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE clearlysaid_usage_events
            SET status = 'completed',
                output_character_count = @outputCharacterCount,
                provider = @provider,
                model = @model,
                latency_milliseconds = @latencyMilliseconds,
                succeeded = true,
                fallback_used = @fallbackUsed,
                failure_reason = @failureReason
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("outputCharacterCount", result.Message.Length);
        command.Parameters.AddWithValue("provider", (object?)result.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("model", (object?)result.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("latencyMilliseconds", (object?)result.LatencyMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("fallbackUsed", result.FallbackUsed);
        command.Parameters.AddWithValue("failureReason", (object?)result.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("id", usageId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task ReleaseUsageAsync(long usageId, string failureReason, CancellationToken cancellationToken) =>
        SetUsageStatusAsync(usageId, "failed", failureReason, cancellationToken);

    public async Task<IReadOnlyList<AdminUser>> GetAdminUsersAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id, u.email, u.role, q.plan_id, q.monthly_allowance,
                   count(e.id) FILTER (WHERE e.status IN ('reserved', 'completed'))::int,
                   u.disabled_at IS NOT NULL, u.created_at
            FROM clearlysaid_users u
            JOIN clearlysaid_entitlements q ON q.user_id = u.id
            LEFT JOIN clearlysaid_usage_events e ON e.user_id = u.id
                 AND e.occurred_at >= q.period_started_at AND e.occurred_at < q.period_ends_at
            GROUP BY u.id, u.email, u.role, q.plan_id, q.monthly_allowance, u.disabled_at, u.created_at
            ORDER BY u.created_at DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var users = new List<AdminUser>();
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new AdminUser(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetBoolean(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return users;
    }

    public async Task<AdminUser?> CreateAdminUserAsync(
        CreateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = new UserCredential(Guid.NewGuid(), normalizedEmail);
        var passwordHash = passwordHasher.HashPassword(user, request.Password);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO clearlysaid_users (id, email, normalized_email, password_hash, role)
                VALUES (@id, @email, @normalizedEmail, @passwordHash, @role);
                INSERT INTO clearlysaid_entitlements
                    (user_id, plan_id, monthly_allowance, status, period_started_at, period_ends_at)
                VALUES (@id, @plan, @allowance, 'active', date_trunc('month', now()), date_trunc('month', now()) + interval '1 month');
                """;
            command.Parameters.AddWithValue("id", user.Id);
            command.Parameters.AddWithValue("email", request.Email.Trim());
            command.Parameters.AddWithValue("normalizedEmail", normalizedEmail);
            command.Parameters.AddWithValue("passwordHash", passwordHash);
            command.Parameters.AddWithValue("role", request.Role);
            command.Parameters.AddWithValue("plan", request.Plan.Trim());
            command.Parameters.AddWithValue("allowance", request.MonthlyAllowance);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (await GetAdminUsersAsync(cancellationToken)).Single(x => x.Id == user.Id);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
    }

    public async Task<AdminUser?> UpdateAdminUserAsync(
        Guid userId,
        UpdateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAdminRemainsAsync(connection, transaction, userId, request.Role, request.IsDisabled, cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE clearlysaid_users
                SET email = @email, normalized_email = @normalizedEmail, role = @role,
                    disabled_at = CASE WHEN @disabled THEN coalesce(disabled_at, now()) ELSE NULL END
                WHERE id = @id;
                UPDATE clearlysaid_entitlements
                SET plan_id = @plan, monthly_allowance = @allowance,
                    status = CASE WHEN @disabled THEN 'disabled' ELSE 'active' END,
                    updated_at = now()
                WHERE user_id = @id;
                UPDATE clearlysaid_access_tokens SET revoked_at = now()
                WHERE user_id = @id AND @disabled AND revoked_at IS NULL;
                """;
            command.Parameters.AddWithValue("id", userId);
            command.Parameters.AddWithValue("email", request.Email.Trim());
            command.Parameters.AddWithValue("normalizedEmail", NormalizeEmail(request.Email));
            command.Parameters.AddWithValue("role", request.Role);
            command.Parameters.AddWithValue("plan", request.Plan.Trim());
            command.Parameters.AddWithValue("allowance", request.MonthlyAllowance);
            command.Parameters.AddWithValue("disabled", request.IsDisabled);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return affected == 0 ? null : (await GetAdminUsersAsync(cancellationToken)).SingleOrDefault(x => x.Id == userId);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
    }

    public async Task<bool> ResetAdminPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = new UserCredential(userId, string.Empty);
        var passwordHash = passwordHasher.HashPassword(user, password);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE clearlysaid_users SET password_hash = @passwordHash WHERE id = @id;
            UPDATE clearlysaid_access_tokens SET revoked_at = now()
            WHERE user_id = @id AND revoked_at IS NULL;
            """;
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("passwordHash", passwordHash);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task DeleteAdminUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAdminRemainsAsync(connection, transaction, userId, AccountRoles.User, true, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE clearlysaid_users
            SET email = 'deleted-' || id::text || '@invalid.local',
                normalized_email = 'DELETED-' || id::text,
                password_hash = 'DELETED', role = 'User', disabled_at = now()
            WHERE id = @id;
            UPDATE clearlysaid_entitlements SET status = 'disabled', updated_at = now() WHERE user_id = @id;
            UPDATE clearlysaid_access_tokens SET revoked_at = now() WHERE user_id = @id AND revoked_at IS NULL;
            """;
        command.Parameters.AddWithValue("id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordDiagnosticAsync(
        string severity,
        string category,
        string eventName,
        string message,
        Guid? userId,
        Guid? requestId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clearlysaid_diagnostic_events
                (severity, category, event_name, message, user_id, request_id)
            VALUES (@severity, @category, @eventName, @message, @userId, @requestId);
            """;
        command.Parameters.AddWithValue("severity", severity[..Math.Min(severity.Length, 20)]);
        command.Parameters.AddWithValue("category", category[..Math.Min(category.Length, 100)]);
        command.Parameters.AddWithValue("eventName", eventName[..Math.Min(eventName.Length, 100)]);
        command.Parameters.AddWithValue("message", message[..Math.Min(message.Length, 1000)]);
        command.Parameters.Add("userId", NpgsqlDbType.Uuid).Value = (object?)userId ?? DBNull.Value;
        command.Parameters.Add("requestId", NpgsqlDbType.Uuid).Value = (object?)requestId ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminDiagnosticEvent>> GetAdminDiagnosticsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.id, e.occurred_at,
                   CASE WHEN e.succeeded THEN 'Information' ELSE 'Error' END,
                   'Refinement', 'Submission ' || e.status, u.email, e.request_id,
                   e.character_count, e.estimated_input_tokens, e.output_character_count,
                   e.provider, e.model, e.latency_milliseconds, e.succeeded,
                   e.fallback_used, e.failure_reason
            FROM clearlysaid_usage_events e
            LEFT JOIN clearlysaid_users u ON u.id = e.user_id
            UNION ALL
            SELECT -d.id, d.occurred_at, d.severity, d.category, d.event_name, u.email,
                   d.request_id, NULL::integer, NULL::integer, NULL::integer,
                   NULL::text, NULL::text, NULL::bigint,
                   d.severity NOT IN ('Error', 'Critical'), false, d.message
            FROM clearlysaid_diagnostic_events d
            LEFT JOIN clearlysaid_users u ON u.id = d.user_id
            ORDER BY 2 DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<AdminDiagnosticEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new AdminDiagnosticEvent(
                reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetInt64(12), reader.GetBoolean(13), reader.GetBoolean(14),
                reader.IsDBNull(15) ? null : reader.GetString(15)));
        }

        return events;
    }

    public async Task RevokeTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clearlysaid_access_tokens SET revoked_at = now() WHERE token_hash = @hash;";
        command.Parameters.AddWithValue("hash", HashToken(accessToken));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE clearlysaid_users
            SET email = 'deleted-' || id::text || '@invalid.local',
                normalized_email = 'DELETED-' || id::text,
                password_hash = 'DELETED',
                disabled_at = now()
            WHERE id = @userId;
            UPDATE clearlysaid_access_tokens SET revoked_at = now() WHERE user_id = @userId;
            """;
        command.Parameters.AddWithValue("userId", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<(string Token, DateTimeOffset ExpiresAt)> CreateSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO clearlysaid_access_tokens (id, user_id, token_hash, expires_at)
                VALUES (@id, @userId, @hash, @expiresAt);
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("hash", HashToken(token));
            command.Parameters.AddWithValue("expiresAt", expiresAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return (token, expiresAt);
    }

    private static async Task EnsureAdminRemainsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        string newRole,
        bool disabled,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT role, disabled_at IS NOT NULL,
                   (SELECT count(*) FROM clearlysaid_users
                    WHERE role = 'Admin' AND disabled_at IS NULL)
            FROM clearlysaid_users WHERE id = @id FOR UPDATE;
            """;
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException("The user was not found.");
        }

        var isActiveAdmin = reader.GetString(0) == AccountRoles.Admin && !reader.GetBoolean(1);
        var adminCount = reader.GetInt64(2);
        if (isActiveAdmin && (disabled || newRole != AccountRoles.Admin) && adminCount <= 1)
        {
            throw new InvalidOperationException("The last active administrator cannot be disabled, deleted, or demoted.");
        }
    }

    private async Task EnsureCurrentPeriodAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE clearlysaid_entitlements
            SET period_started_at = date_trunc('month', now()),
                period_ends_at = date_trunc('month', now()) + interval '1 month',
                updated_at = now()
            WHERE user_id = @userId AND period_ends_at <= now();
            """;
        command.Parameters.AddWithValue("userId", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SetUsageStatusAsync(
        long usageId,
        string status,
        string failureReason,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE clearlysaid_usage_events
            SET status = @status, succeeded = false, failure_reason = @failureReason
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue(
            "failureReason",
            failureReason.Length <= 500 ? failureReason : failureReason[..500]);
        command.Parameters.AddWithValue("id", usageId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static AccountInfo ReadAccount(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
        reader.GetInt32(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6));

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private const string AccountSql = """
        SELECT u.id, u.email, q.plan_id, q.monthly_allowance,
               count(e.id) FILTER (WHERE e.status IN ('reserved', 'completed'))::int AS used,
               q.period_ends_at, u.role
        FROM clearlysaid_users u
        JOIN clearlysaid_entitlements q ON q.user_id = u.id
        LEFT JOIN clearlysaid_usage_events e ON e.user_id = u.id
             AND e.occurred_at >= q.period_started_at AND e.occurred_at < q.period_ends_at
        WHERE u.id = @userId
        GROUP BY u.id, u.email, q.plan_id, q.monthly_allowance, q.period_ends_at, u.role;
        """;

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS clearlysaid_users (
            id uuid PRIMARY KEY,
            email text NOT NULL,
            normalized_email text NOT NULL UNIQUE,
            password_hash text NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            disabled_at timestamptz NULL
        );
        ALTER TABLE clearlysaid_users ADD COLUMN IF NOT EXISTS role text NOT NULL DEFAULT 'User';
        CREATE TABLE IF NOT EXISTS clearlysaid_entitlements (
            user_id uuid PRIMARY KEY REFERENCES clearlysaid_users(id) ON DELETE CASCADE,
            plan_id text NOT NULL,
            monthly_allowance integer NOT NULL CHECK (monthly_allowance >= 0),
            status text NOT NULL,
            provider text NULL,
            provider_reference text NULL,
            period_started_at timestamptz NOT NULL,
            period_ends_at timestamptz NOT NULL,
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE TABLE IF NOT EXISTS clearlysaid_access_tokens (
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL REFERENCES clearlysaid_users(id) ON DELETE CASCADE,
            token_hash bytea NOT NULL UNIQUE,
            expires_at timestamptz NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            revoked_at timestamptz NULL
        );
        CREATE INDEX IF NOT EXISTS ix_clearlysaid_access_tokens_user ON clearlysaid_access_tokens(user_id);
        CREATE TABLE IF NOT EXISTS clearlysaid_usage_events (
            id bigserial PRIMARY KEY,
            user_id uuid NOT NULL REFERENCES clearlysaid_users(id) ON DELETE CASCADE,
            occurred_at timestamptz NOT NULL DEFAULT now(),
            character_count integer NOT NULL,
            status text NOT NULL
        );
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS request_id uuid NULL;
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS estimated_input_tokens integer NULL;
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS output_character_count integer NULL;
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS provider text NULL;
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS model text NULL;
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS latency_milliseconds bigint NULL;
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS succeeded boolean NOT NULL DEFAULT false;
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS fallback_used boolean NOT NULL DEFAULT false;
        ALTER TABLE clearlysaid_usage_events ADD COLUMN IF NOT EXISTS failure_reason text NULL;
        CREATE INDEX IF NOT EXISTS ix_clearlysaid_usage_user_time ON clearlysaid_usage_events(user_id, occurred_at);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_clearlysaid_usage_user_request
            ON clearlysaid_usage_events(user_id, request_id) WHERE request_id IS NOT NULL;
        CREATE TABLE IF NOT EXISTS clearlysaid_diagnostic_events (
            id bigserial PRIMARY KEY,
            occurred_at timestamptz NOT NULL DEFAULT now(),
            severity text NOT NULL,
            category text NOT NULL,
            event_name text NOT NULL,
            message text NOT NULL,
            user_id uuid NULL REFERENCES clearlysaid_users(id) ON DELETE SET NULL,
            request_id uuid NULL
        );
        CREATE INDEX IF NOT EXISTS ix_clearlysaid_diagnostics_time
            ON clearlysaid_diagnostic_events(occurred_at DESC);
        """;
}

public sealed record UserCredential(Guid Id, string NormalizedEmail);
public sealed record AuthenticatedUser(Guid Id, string Email, string Role);
public sealed record UsageReservation(long? UsageId, bool Duplicate);
