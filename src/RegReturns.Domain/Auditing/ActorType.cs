namespace RegReturns.Domain.Auditing;

/// <summary>The kind of party that performed an audited action.</summary>
public enum ActorType
{
    /// <summary>A person signed in through WSO2.</summary>
    User = 1,

    /// <summary>A bank system using a client-credentials token.</summary>
    ApiClient = 2,

    /// <summary>The application itself (jobs, seeding, demo reset).</summary>
    System = 3,

    /// <summary>A caller whose identity could not be established.</summary>
    Anonymous = 4,
}
