using RegReturns.Domain.Common;

namespace RegReturns.Application.Migration;

/// <summary>
/// Stable codes of the legacy migration: <see cref="Error"/>s that stop a run before it reads any row, and the codes of
/// row errors in the error report (ADR 0029, docs/DATA-MIGRATION.md).
/// </summary>
public static class MigrationErrors
{
    /// <summary>Row code: the row has a different number of cells from the header.</summary>
    public const string ColumnCount = "Legacy.ColumnCount";

    /// <summary>Row code: the bank name is not in the mapping.</summary>
    public const string UnknownInstitution = "Legacy.UnknownInstitution";

    /// <summary>Row code: the mapping names a bank code the portal does not know.</summary>
    public const string InstitutionNotInPortal = "Legacy.InstitutionNotInPortal";

    /// <summary>Row code: a date matches none of the mapping's formats.</summary>
    public const string BadDate = "Legacy.BadDate";

    /// <summary>Row code: the reporting date is not the last day of a month or quarter.</summary>
    public const string NotPeriodEnd = "Legacy.NotPeriodEnd";

    /// <summary>Row code: the filing and approval dates are out of order or in the future.</summary>
    public const string InconsistentDates = "Legacy.InconsistentDates";

    /// <summary>Row code: a value is not a number after cleansing.</summary>
    public const string BadNumber = "Legacy.BadNumber";

    /// <summary>Row code: no published template version applies to the period.</summary>
    public const string NoTemplate = "Legacy.NoTemplate";

    /// <summary>Row code: the portal already holds a return for the period that was not migrated.</summary>
    public const string AlreadyFiled = "Legacy.AlreadyFiled";

    /// <summary>Row code: the values break one of the template's error rules.</summary>
    public const string Validation = "Legacy.Validation";

    /// <summary>Row code: the domain refused the migrated return for another reason.</summary>
    public const string Refused = "Legacy.Refused";

    /// <summary>Row code: a later row for the same bank, return type and period replaced this one.</summary>
    public const string Superseded = "Legacy.Superseded";

    /// <summary>The source folder or mapping file does not exist.</summary>
    public static readonly Error SourceNotFound = new(
        "Migration.SourceNotFound", "The source folder or mapping file does not exist.");

    /// <summary>The mapping file is not valid.</summary>
    public static readonly Error MappingInvalid = new("Migration.MappingInvalid", "The mapping file is not valid.");

    /// <summary>A file cannot be read as UTF-8 CSV, or it is too large.</summary>
    public static readonly Error FileUnreadable = new("Migration.FileUnreadable", "A legacy file cannot be read as CSV.");

    /// <summary>The source folder and the mapping do not list the same files, or a header does not match the mapping.</summary>
    public static readonly Error SourceMismatch = new(
        "Migration.SourceMismatch", "The legacy files do not match the mapping.");
}
