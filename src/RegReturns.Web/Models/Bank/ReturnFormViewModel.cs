using RegReturns.Application.Returns;

namespace RegReturns.Web.Models.Bank;

/// <summary>The return entry page: the saved return, plus what the user posted when a save, justification or submission failed.</summary>
public sealed class ReturnFormViewModel
{
    /// <summary>Initializes a new instance of the <see cref="ReturnFormViewModel"/> class showing the saved return.</summary>
    /// <param name="form">The return as saved.</param>
    public ReturnFormViewModel(ReturnForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        Form = form;
        EditVersion = form.EditVersion;
    }

    /// <summary>Gets the return as saved.</summary>
    public ReturnForm Form { get; }

    /// <summary>Gets the edit counter the form sends back when saving.</summary>
    public int EditVersion { get; init; }

    /// <summary>Gets the values the user posted, shown instead of the saved ones after a failed save.</summary>
    public IReadOnlyDictionary<string, string?>? PostedValues { get; init; }

    /// <summary>Gets why the save failed.</summary>
    public string? SaveError { get; init; }

    /// <summary>Gets a value indicating whether the save failed because someone else saved first.</summary>
    public bool IsEditConflict { get; init; }

    /// <summary>Gets the finding whose justification failed.</summary>
    public Guid? JustifyFindingId { get; init; }

    /// <summary>Gets the justification the user posted.</summary>
    public string? JustifyText { get; init; }

    /// <summary>Gets why the justification failed.</summary>
    public string? JustifyError { get; init; }

    /// <summary>Gets the comment the checker posted when the submission was refused.</summary>
    public string? SubmitComment { get; init; }

    /// <summary>Gets why the submission was refused.</summary>
    public string? SubmitError { get; init; }

    /// <summary>Gets the value to show in a field's input.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The posted value after a failed save, otherwise the saved one.</returns>
    public string? ValueOf(ReturnFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return PostedValues is null ? field.Value : PostedValues.GetValueOrDefault(field.Code);
    }
}

/// <summary>The posted return entry form.</summary>
public sealed class ReturnValuesInput
{
    /// <summary>Gets or sets the edit counter the page was rendered with.</summary>
    public int? EditVersion { get; set; }

    /// <summary>Gets the values keyed by field code (posted as <c>Values[CODE]</c>).</summary>
    public Dictionary<string, string?> Values { get; } = new(StringComparer.Ordinal);
}
