namespace TaikoDiveLauncher.Models;

public interface IUnsavedChangesAware
{
    bool HasUnsavedChanges { get; }

    string UnsavedChangesName { get; }
}
