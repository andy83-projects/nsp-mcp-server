namespace Nsp.Automation.Core;

/// <summary>
/// Application-level NSP commands: open/save, Function Manager, I/O assignments, generic
/// scalar fields, validation-error listing. One concrete implementation is wired up via DI;
/// if Phase 0 finds parts of the UI need the fallback path, swap the implementation here
/// rather than branching per call.
/// </summary>
public interface IAppDriver
{
    Task<AutomationResult> GetAppStateAsync(CancellationToken cancellationToken = default);

    Task<AutomationResult> OpenMapAsync(string filePath, CancellationToken cancellationToken = default);

    Task<AutomationResult> SaveMapAsync(bool confirm, CancellationToken cancellationToken = default);

    Task<AutomationResult> OpenFunctionManagerAsync(CancellationToken cancellationToken = default);

    Task<AutomationResult> SetFunctionEnabledAsync(string functionName, bool enabled, bool confirm, CancellationToken cancellationToken = default);

    Task<AutomationResult> GetFunctionStateAsync(string functionName, CancellationToken cancellationToken = default);

    Task<AutomationResult> SetIoAssignmentAsync(string functionName, string connectionField, string pinOrResourceId, bool confirm, CancellationToken cancellationToken = default);

    Task<AutomationResult> GetIoAssignmentAsync(string functionName, string connectionField, CancellationToken cancellationToken = default);

    Task<AutomationResult> SetFieldValueAsync(string dialogPath, string fieldLabel, string value, bool confirm, CancellationToken cancellationToken = default);

    Task<AutomationResult> GetFieldValueAsync(string dialogPath, string fieldLabel, CancellationToken cancellationToken = default);

    Task<AutomationResult> ListValidationErrorsAsync(CancellationToken cancellationToken = default);
}
