using System;

namespace GameLauncher.Domain;

// Directional association: launching EntryId also requests CompanionId, not vice versa.
public sealed class LaunchCompanion
{
    public Guid EntryId { get; set; }
    public Guid CompanionId { get; set; }
}
