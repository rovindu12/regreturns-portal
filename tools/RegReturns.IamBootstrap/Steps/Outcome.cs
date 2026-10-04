namespace RegReturns.IamBootstrap.Steps;

/// <summary>What an idempotent "ensure" call did.</summary>
internal enum Outcome
{
    /// <summary>The object already matched the desired state.</summary>
    Unchanged = 0,

    /// <summary>The object was created.</summary>
    Created,

    /// <summary>The object existed and was brought to the desired state.</summary>
    Updated,
}
