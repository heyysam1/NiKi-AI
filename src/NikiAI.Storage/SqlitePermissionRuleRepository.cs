using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Security;
using NikiAI.Core.Tools;

namespace NikiAI.Storage;

/// <summary>
/// SQLite implementation of IPermissionRuleRepository.
/// Manages persistent permission rules scoped narrowly by scope key (toolId:action:resource).
/// Fails closed if the database cannot be read.
/// </summary>
public class SqlitePermissionRuleRepository : IPermissionRuleRepository
{
    private readonly StorageContext _storageContext;
    private readonly ILogger<SqlitePermissionRuleRepository>? _logger;

    public SqlitePermissionRuleRepository(StorageContext storageContext, ILogger<SqlitePermissionRuleRepository>? logger = null)
    {
        _storageContext = storageContext ?? throw new ArgumentNullException(nameof(storageContext));
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, ApprovalDecision>> LoadRulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = new Dictionary<string, ApprovalDecision>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var connection = _storageContext.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT scope_key, decision FROM permission_rules;";

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var scopeKey = reader.GetString(0);
                var decisionVal = reader.GetInt32(1);
                rules[scopeKey] = (ApprovalDecision)decisionVal;
            }

            _logger?.LogInformation("Loaded {Count} persistent permission rules from database.", rules.Count);
            return rules;
        }
        catch (Exception ex)
        {
            // Correction 8: Fail closed! Never assume an Allow decision when store read fails.
            _logger?.LogError(ex, "Failed to load persistent permission rules from database. Failing closed with empty rules.");
            return new Dictionary<string, ApprovalDecision>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task SaveRuleAsync(
        string scopeKey,
        string toolId,
        ApprovalDecision decision,
        ToolRiskLevel riskLevel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);
        ArgumentNullException.ThrowIfNull(toolId);

        try
        {
            using var connection = _storageContext.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO permission_rules (scope_key, tool_id, decision, risk_level, created_at, updated_at)
                VALUES ($scopeKey, $toolId, $decision, $riskLevel, $now, $now)
                ON CONFLICT(scope_key) DO UPDATE SET
                    decision = $decision,
                    risk_level = $riskLevel,
                    updated_at = $now;
            ";

            var now = DateTimeOffset.UtcNow.ToString("O");
            cmd.Parameters.AddWithValue("$scopeKey", scopeKey);
            cmd.Parameters.AddWithValue("$toolId", toolId);
            cmd.Parameters.AddWithValue("$decision", (int)decision);
            cmd.Parameters.AddWithValue("$riskLevel", (int)riskLevel);
            cmd.Parameters.AddWithValue("$now", now);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger?.LogInformation("Saved persistent permission rule: Scope='{ScopeKey}', Tool='{ToolId}', Decision='{Decision}'.",
                scopeKey, toolId, decision);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save persistent permission rule for '{ScopeKey}'.", scopeKey);
            throw;
        }
    }

    public async Task<bool> DeleteRuleAsync(string scopeKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeKey);

        try
        {
            using var connection = _storageContext.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM permission_rules WHERE scope_key = $scopeKey;";
            cmd.Parameters.AddWithValue("$scopeKey", scopeKey);

            var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger?.LogInformation("Deleted persistent permission rule for Scope='{ScopeKey}' (Rows: {Rows}).", scopeKey, rowsAffected);
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete persistent permission rule for '{ScopeKey}'.", scopeKey);
            return false;
        }
    }

    public async Task ClearAllRulesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _storageContext.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM permission_rules;";

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger?.LogInformation("Cleared all persistent permission rules.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to clear persistent permission rules.");
            throw;
        }
    }
}
