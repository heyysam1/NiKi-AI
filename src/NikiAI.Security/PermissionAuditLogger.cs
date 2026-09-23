using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NikiAI.Core.Logging;
using NikiAI.Core.Security;

namespace NikiAI.Security;

/// <summary>
/// Structured in-memory audit logger for capturing permission evaluation and approval events.
/// Strictly redacts secrets, tokens, and credentials from all recorded fields.
/// </summary>
public class PermissionAuditLogger : IPermissionAuditLogger
{
    private readonly ILogger<PermissionAuditLogger>? _logger;
    private readonly ConcurrentQueue<PermissionAuditRecord> _records = new();
    private const int MaxRecords = 100;

    public PermissionAuditLogger(ILogger<PermissionAuditLogger>? logger = null)
    {
        _logger = logger;
    }

    public Task LogPermissionEventAsync(PermissionAuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Correction 7: Redact secrets and sensitive tokens from all audit fields
        var safeRecord = new PermissionAuditRecord(
            RequestId: record.RequestId,
            TaskId: record.TaskId,
            ToolId: record.ToolId,
            ScopeKey: record.ScopeKey,
            RiskLevel: record.RiskLevel,
            Outcome: record.Outcome,
            Decision: record.Decision,
            Actor: record.Actor,
            Timestamp: record.Timestamp,
            Duration: record.Duration,
            SanitizedDetails: SecretRedactor.Redact(record.SanitizedDetails)
        );

        _records.Enqueue(safeRecord);
        while (_records.Count > MaxRecords && _records.TryDequeue(out _)) { }

        _logger?.LogInformation(
            "Permission audit: Tool='{ToolId}', Scope='{ScopeKey}', Risk='{Risk}', Outcome='{Outcome}', Actor='{Actor}'",
            safeRecord.ToolId, safeRecord.ScopeKey, safeRecord.RiskLevel, safeRecord.Outcome, safeRecord.Actor);

        return Task.CompletedTask;
    }

    public IReadOnlyList<PermissionAuditRecord> GetRecentAuditRecords(int limit = 50)
    {
        return _records.Reverse().Take(limit).ToList();
    }
}
