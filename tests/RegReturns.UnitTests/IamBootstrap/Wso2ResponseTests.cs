extern alias IamBootstrapTool;

using System.Net;
using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class Wso2ResponseTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    public void Two_hundred_range_is_success(HttpStatusCode status)
    {
        Response(status).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void Other_statuses_are_not_success(HttpStatusCode status)
    {
        Response(status).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Error_code_comes_from_the_management_api_code()
    {
        Response(HttpStatusCode.Conflict, """{"code":"APP-60007","message":"Already exists"}""").ErrorCode.ShouldBe("APP-60007");
    }

    [Fact]
    public void Error_code_falls_back_to_the_scim_type()
    {
        Response(HttpStatusCode.Conflict, """{"scimType":"uniqueness","detail":"User exists","status":"409"}""").ErrorCode.ShouldBe("uniqueness");
    }

    [Fact]
    public void Error_code_is_missing_without_an_object_body()
    {
        Response(HttpStatusCode.BadGateway, """["not","an","object"]""").ErrorCode.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"description":"from description","detail":"d","message":"m"}""", "from description")]
    [InlineData("""{"detail":"from detail","message":"m"}""", "from detail")]
    [InlineData("""{"message":"from message"}""", "from message")]
    public void Error_description_prefers_description_then_detail_then_message(string body, string expected)
    {
        Response(HttpStatusCode.BadRequest, body).ErrorDescription.ShouldBe(expected);
    }

    [Theory]
    [InlineData("https://iam.valoria.test/t/carbon.super/api/server/v1/applications/6a1f0e2c")]
    [InlineData("https://iam.valoria.test/api/server/v1/applications/6a1f0e2c/")]
    [InlineData("/api/server/v1/applications/6a1f0e2c")]
    public void Location_id_is_the_last_path_segment(string location)
    {
        var response = new Wso2Response("POST", "api/server/v1/applications", HttpStatusCode.Created, null, new Uri(location, UriKind.RelativeOrAbsolute));

        response.LocationId.ShouldBe("6a1f0e2c");
    }

    [Fact]
    public void Location_id_is_missing_without_a_location()
    {
        Response(HttpStatusCode.Created).LocationId.ShouldBeNull();
    }

    [Fact]
    public void EnsureSuccess_returns_a_successful_response()
    {
        var response = Response(HttpStatusCode.Created);

        response.EnsureSuccess().ShouldBeSameAs(response);
    }

    [Fact]
    public void EnsureSuccess_accepts_statuses_the_caller_handles()
    {
        var response = Response(HttpStatusCode.Conflict);

        response.EnsureSuccess(HttpStatusCode.NotFound, HttpStatusCode.Conflict).ShouldBeSameAs(response);
    }

    [Fact]
    public void EnsureSuccess_throws_for_an_unexpected_status()
    {
        var response = Response(HttpStatusCode.BadRequest, """{"code":"CMT-60004","description":"Claim already mapped"}""");

        var failure = Should.Throw<Wso2ApiException>(() => response.EnsureSuccess(HttpStatusCode.Conflict));

        failure.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        failure.ErrorCode.ShouldBe("CMT-60004");
        failure.Message.ShouldBe("WSO2 POST api/server/v1/claim-dialects returned 400 CMT-60004: Claim already mapped");
    }

    [Fact]
    public void Failure_message_carries_no_other_part_of_the_body()
    {
        var response = Response(HttpStatusCode.InternalServerError, """{"code":"X","description":"Failed","clientSecret":"s3cr3t"}""");

        Should.Throw<Wso2ApiException>(() => response.EnsureSuccess()).Message.ShouldNotContain("s3cr3t");
    }

    [Fact]
    public void Failure_without_a_body_still_names_the_call_and_status()
    {
        Should.Throw<Wso2ApiException>(() => Response(HttpStatusCode.ServiceUnavailable).EnsureSuccess())
            .Message.ShouldStartWith("WSO2 POST api/server/v1/claim-dialects returned 503");
    }

    private static Wso2Response Response(HttpStatusCode status, string? body = null) =>
        new("POST", "api/server/v1/claim-dialects", status, body is null ? null : JsonNode.Parse(body), null);
}
