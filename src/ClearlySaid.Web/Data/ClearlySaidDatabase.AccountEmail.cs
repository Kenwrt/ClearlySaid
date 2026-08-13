using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace ClearlySaid.Web.Data;

public sealed partial class ClearlySaidDatabase
{
    public async Task<(Guid UserId, string Token)> CreateAccountTokenAsync(
        string email, string purpose, TimeSpan lifetime, bool requireUnverified,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = """
            SELECT id FROM clearlysaid_users
            WHERE normalized_email = @email AND disabled_at IS NULL
              AND (@requireUnverified = false OR email_verified_at IS NULL);
            """;
        find.Parameters.AddWithValue("email", NormalizeEmail(email));
        find.Parameters.AddWithValue("requireUnverified", requireUnverified);
        var value = await find.ExecuteScalarAsync(cancellationToken);
        if (value is not Guid userId) return (Guid.Empty, string.Empty);

        await using var revoke = connection.CreateCommand();
        revoke.Transaction = transaction;
        revoke.CommandText = """
            UPDATE clearlysaid_account_tokens SET consumed_at = now()
            WHERE user_id = @userId AND purpose = @purpose AND consumed_at IS NULL;
            """;
        revoke.Parameters.AddWithValue("userId", userId);
        revoke.Parameters.AddWithValue("purpose", purpose);
        await revoke.ExecuteNonQueryAsync(cancellationToken);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO clearlysaid_account_tokens (id, user_id, purpose, token_hash, expires_at)
            VALUES (@id, @userId, @purpose, @hash, @expiresAt);
            """;
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("userId", userId);
        insert.Parameters.AddWithValue("purpose", purpose);
        insert.Parameters.AddWithValue("hash", HashToken(token));
        insert.Parameters.AddWithValue("expiresAt", DateTimeOffset.UtcNow.Add(lifetime));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (userId, token);
    }

    public async Task<string?> VerifyEmailAsync(string token, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE clearlysaid_account_tokens t SET consumed_at = now()
            FROM clearlysaid_users u
            WHERE t.user_id = u.id AND t.purpose = 'verify_email' AND t.token_hash = @hash
              AND t.consumed_at IS NULL AND t.expires_at > now() AND u.disabled_at IS NULL
            RETURNING u.id, u.email;
            """;
        command.Parameters.AddWithValue("hash", HashToken(token));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var userId = reader.GetGuid(0);
        var email = reader.GetString(1);
        await reader.CloseAsync();
        await using var activate = connection.CreateCommand();
        activate.Transaction = transaction;
        activate.CommandText = "UPDATE clearlysaid_users SET email_verified_at = coalesce(email_verified_at, now()) WHERE id = @id;";
        activate.Parameters.AddWithValue("id", userId);
        await activate.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return email;
    }

    public async Task<bool> ResetPasswordWithTokenAsync(
        string token, string password, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = """
            SELECT t.id, u.id, u.normalized_email FROM clearlysaid_account_tokens t
            JOIN clearlysaid_users u ON u.id = t.user_id
            WHERE t.purpose = 'password_reset' AND t.token_hash = @hash
              AND t.consumed_at IS NULL AND t.expires_at > now() AND u.disabled_at IS NULL;
            """;
        find.Parameters.AddWithValue("hash", HashToken(token));
        await using var reader = await find.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return false;
        var tokenId = reader.GetGuid(0);
        var user = new UserCredential(reader.GetGuid(1), reader.GetString(2));
        await reader.CloseAsync();
        var hash = passwordHasher.HashPassword(user, password);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE clearlysaid_users SET password_hash = @passwordHash WHERE id = @userId;
            UPDATE clearlysaid_account_tokens SET consumed_at = now() WHERE id = @tokenId;
            UPDATE clearlysaid_access_tokens SET revoked_at = now()
            WHERE user_id = @userId AND revoked_at IS NULL;
            """;
        update.Parameters.AddWithValue("passwordHash", hash);
        update.Parameters.AddWithValue("userId", user.Id);
        update.Parameters.AddWithValue("tokenId", tokenId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<string?> AcceptInvitationAsync(
        string token, string password, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = """
            SELECT t.id, u.id, u.normalized_email, u.email FROM clearlysaid_account_tokens t
            JOIN clearlysaid_users u ON u.id = t.user_id
            WHERE t.purpose = 'invitation' AND t.token_hash = @hash
              AND t.consumed_at IS NULL AND t.expires_at > now()
              AND u.disabled_at IS NULL AND u.email_verified_at IS NULL;
            """;
        find.Parameters.AddWithValue("hash", HashToken(token));
        await using var reader = await find.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var tokenId = reader.GetGuid(0);
        var user = new UserCredential(reader.GetGuid(1), reader.GetString(2));
        var email = reader.GetString(3);
        await reader.CloseAsync();

        var hash = passwordHasher.HashPassword(user, password);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE clearlysaid_users
            SET password_hash = @passwordHash, email_verified_at = now()
            WHERE id = @userId;
            UPDATE clearlysaid_account_tokens SET consumed_at = now() WHERE id = @tokenId;
            """;
        update.Parameters.AddWithValue("passwordHash", hash);
        update.Parameters.AddWithValue("userId", user.Id);
        update.Parameters.AddWithValue("tokenId", tokenId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return email;
    }
}
