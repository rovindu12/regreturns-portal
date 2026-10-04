namespace RegReturns.Domain.Templates;

/// <summary>Lifecycle of a template version.</summary>
public enum TemplateStatus
{
    /// <summary>Being designed; can be edited; cannot be used for filing.</summary>
    Draft = 1,

    /// <summary>In use for filing; immutable.</summary>
    Published = 2,

    /// <summary>Superseded; kept so historical submissions still render.</summary>
    Retired = 3,
}
