using RegReturns.Api.Problems;
using RegReturns.Application.Abstractions;
using RegReturns.Application.Idempotency;
using RegReturns.Application.Identity;
using RegReturns.Application.Returns;
using RegReturns.Application.Templates;
using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;

namespace RegReturns.UnitTests.Api;

public sealed class ApiProblemsTests
{
    public static TheoryData<string, int> Statuses => new()
    {
        { IdempotencyErrors.KeyRequired.Code, 400 },
        { IdempotencyErrors.KeyInvalid.Code, 400 },
        { ApiProblems.InvalidRequestCode, 400 },
        { ActorErrors.NotLinked.Code, 403 },
        { SubmissionErrors.RoleRequired.Code, 403 },
        { SubmissionErrors.WrongInstitution.Code, 403 },
        { SubmissionErrors.RegulatorOnly.Code, 403 },
        { SubmissionErrors.NotFound.Code, 404 },
        { ReturnTypeErrors.NotFound.Code, 404 },
        { PersistenceErrors.Conflict.Code, 409 },
        { SubmissionErrors.NotEditable.Code, 409 },
        { SubmissionErrors.EditConflict.Code, 409 },
        { DeliveryErrors.Concurrent.Code, 409 },
        { IdempotencyErrors.InProgress.Code, 409 },
        { DeliveryErrors.UnknownFields.Code, 422 },
        { DeliveryErrors.NoObligation.Code, 422 },
        { ReturnTypeErrors.PeriodMismatch.Code, 422 },
        { IdempotencyErrors.KeyReused.Code, 422 },
        { SubmissionErrors.CheckerIsMaker.Code, 422 },
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Each_error_maps_to_the_status_of_its_meaning(string code, int status)
    {
        ApiProblems.StatusFor(new Error(code, "Message.")).ShouldBe(status);
    }
}
