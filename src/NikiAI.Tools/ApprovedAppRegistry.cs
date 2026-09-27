using System.Collections.Concurrent;
using NikiAI.Core.Tools;

namespace NikiAI.Tools;

/// <summary>
/// Controlled registry for approved desktop applications.
/// Enforces strict application allowlisting, eliminates arbitrary executable execution,
/// and strictly blocks restricted command shells (e.g. cmd.exe, powershell.exe).
/// </summary>
public class ApprovedAppRegistry : IApprovedAppRegistry
{
    private static readonly HashSet<string> ProhibitedIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd",
        "cmd.exe",
        "powershell",
        "powershell.exe",
        "pwsh",
        "pwsh.exe",
        "bash",
        "bash.exe",
        "wscript",
        "wscript.exe",
        "cscript",
        "cscript.exe"
    };

    private static readonly char[] ForbiddenPathChars = ['\\', '/', ':', ';', '&', '|', '>', '<', '`', '$', '%'];

    private readonly ConcurrentDictionary<string, ApprovedAppEntry> _entriesByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ApprovedAppEntry> _entriesByAlias = new(StringComparer.OrdinalIgnoreCase);

    public ApprovedAppRegistry()
    {
        RegisterDefaults();
    }

    private void RegisterDefaults()
    {
        // Safe, standard Windows utilities and approved productivity apps
        Register(new ApprovedAppEntry("notepad", "Notepad", "notepad.exe", "Windows Text Editor"), "editor", "text");
        Register(new ApprovedAppEntry("calculator", "Calculator", "calc.exe", "Windows Calculator"), "calc");
        Register(new ApprovedAppEntry("paint", "Paint", "mspaint.exe", "Windows Paint"), "mspaint");
        Register(new ApprovedAppEntry("explorer", "File Explorer", "explorer.exe", "Windows File Explorer"), "files", "folder");
        Register(new ApprovedAppEntry("terminal", "Windows Terminal", "wt.exe", "Windows Terminal"), "wt");
        Register(new ApprovedAppEntry("taskmgr", "Task Manager", "taskmgr.exe", "Windows Task Manager"), "taskmanager");
        Register(new ApprovedAppEntry("vscode", "Visual Studio Code", "code", "Visual Studio Code Editor"), "code");
        Register(new ApprovedAppEntry("edge", "Microsoft Edge", "msedge.exe", "Microsoft Edge Web Browser"), "msedge", "browser");
        Register(new ApprovedAppEntry("brave", "Brave Browser", "brave.exe", "Brave Privacy Web Browser"), "brave-browser");
    }

    public void Register(ApprovedAppEntry entry, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entriesByKey[entry.Key] = entry;
        _entriesByAlias[entry.Key] = entry;

        foreach (var alias in aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                _entriesByAlias[alias.Trim()] = entry;
            }
        }
    }

    public bool IsProhibitedApp(string appNameOrKey, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(appNameOrKey))
        {
            return false;
        }

        var normalized = appNameOrKey.Trim();

        if (ProhibitedIdentifiers.Contains(normalized))
        {
            reason = $"Policy Violation: Executable '{normalized}' is a restricted command shell and cannot be launched directly.";
            return true;
        }

        return false;
    }

    public bool TryResolveApp(string appNameOrKey, out ApprovedAppEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(appNameOrKey))
        {
            return false;
        }

        var trimmed = appNameOrKey.Trim();

        // Strict prohibition check first
        if (IsProhibitedApp(trimmed, out _))
        {
            return false;
        }

        // Reject any attempt to pass arbitrary paths or shell injection characters
        if (trimmed.IndexOfAny(ForbiddenPathChars) >= 0)
        {
            return false;
        }

        // Resolve via key or alias
        if (_entriesByAlias.TryGetValue(trimmed, out entry))
        {
            return true;
        }

        return false;
    }

    public IReadOnlyList<ApprovedAppEntry> GetApprovedApps()
    {
        return _entriesByKey.Values.OrderBy(e => e.DisplayName).ToList();
    }
}
