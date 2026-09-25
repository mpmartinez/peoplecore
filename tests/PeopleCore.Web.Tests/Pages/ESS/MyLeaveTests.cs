using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.ESS;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.ESS;

public class MyLeaveTests : BunitContext
{
    private static readonly Guid EmployeeId = Guid.Parse("3f6a9d21-8c4b-4e0f-a7d2-5b1e9c3f7a01");
    private static readonly Guid VacationTypeId = Guid.Parse("a1c0e5d7-2b3f-4c8e-9d1a-6f4b7e2c0d11");
    private static readonly Guid SoloParentTypeId = Guid.Parse("a1c0e5d7-2b3f-4c8e-9d1a-6f4b7e2c0d13");
    private static readonly Guid PaternityTypeId = Guid.Parse("a1c0e5d7-2b3f-4c8e-9d1a-6f4b7e2c0d14");
    private static readonly Guid MaternityTypeId = Guid.Parse("a1c0e5d7-2b3f-4c8e-9d1a-6f4b7e2c0d15");
    private static readonly Guid NewRequestId = Guid.Parse("b2d1f6e8-3c4a-4d9f-8e2b-7a5c8f3d1e01");
    private static readonly Guid PendingRequestId = Guid.Parse("b2d1f6e8-3c4a-4d9f-8e2b-7a5c8f3d1e02");

    private const string OptionsPath = "/api/leave-requests/options";
    private const string FileRefusal = "Attach a PDF, JPG or PNG of at most 10 MB.";

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    private string _balancesJson = "[]";
    private string _requestsJson = Paged();
    private string _optionsJson = $"[{VacationOption(daysLeft: 12)}]";

    public MyLeaveTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("maria@company.test");
    }

    private static string BalancesPath(Guid employeeId) => $"/api/leave-balances/{employeeId}";

    private static string RequestsPath(Guid employeeId) => $"/api/leave-requests?page=1&pageSize=20&employeeId={employeeId}";

    private static string DocumentPath(Guid requestId) => $"/api/leave-requests/{requestId}/document";

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private static string VacationBalance(int total, int used) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{EmployeeId}}","employeeName":"Maria Santos","leaveTypeId":"{{VacationTypeId}}",
         "leaveTypeName":"Vacation Leave","year":2026,"totalDays":{{total}},"usedDays":{{used}},"carriedOverDays":0,"remainingDays":{{total - used}},
         "isConfidential":false}
        """;

    /// <summary>A LeaveFilingOptionDto as the API sends it: every field, in the record's order.</summary>
    private static string Option(
        Guid id, string name, string code, string kind, bool requiresDocument = false, bool isMaternity = false,
        decimal? daysLeft = null, decimal? daysPerEvent = null, int? maxEvents = null, int eventsUsed = 0,
        bool hasSoloParentBonus = false) =>
        JsonSerializer.Serialize(new
        {
            leaveTypeId = id, name, code, kind, countsCalendarDays = isMaternity, requiresDocument, isMaternity,
            daysLeftThisYear = daysLeft, daysPerEvent, maxEvents, eventsUsed, hasSoloParentBonus,
        });

    private static string VacationOption(decimal daysLeft) =>
        Option(VacationTypeId, "Vacation Leave", "VL", "Accrued", daysLeft: daysLeft);

    private static string SoloParentOption(decimal daysLeft = 7m) =>
        Option(SoloParentTypeId, "Solo Parent Leave", "SPL", "YearlyAllowance", requiresDocument: true, daysLeft: daysLeft);

    private static string PaternityOption(int? maxEvents = null, int eventsUsed = 0) =>
        Option(PaternityTypeId, "Paternity Leave", "PL", "PerEvent", daysPerEvent: 7m, maxEvents: maxEvents, eventsUsed: eventsUsed);

    private static string MaternityOption(bool bonus = false) =>
        Option(MaternityTypeId, "Maternity Leave", "ML", "PerEvent", isMaternity: true, daysPerEvent: 105m, hasSoloParentBonus: bonus);

    private static string Request(string start, string end, int days, string status, Guid? id = null,
        Guid? typeId = null, string typeName = "Vacation Leave", bool hasDocument = false, string? fileName = null) =>
        JsonSerializer.Serialize(new
        {
            id = id ?? Guid.NewGuid(), employeeId = EmployeeId, employeeName = "Maria Santos",
            leaveTypeId = typeId ?? VacationTypeId, leaveTypeName = typeName,
            startDate = start, endDate = end, totalDays = days, reason = (string?)null, status,
            approvedBy = (Guid?)null, approvedAt = (DateTime?)null, rejectionReason = (string?)null,
            createdAt = new DateTime(2026, 9, 20, 1, 0, 0, DateTimeKind.Utc),
            maternityCase = (string?)null, daysAllocatedToFather = 0,
            hasDocument, documentFileName = fileName, isConfidential = false,
        });

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Refusal(string detail) =>
        Json($$"""{"title":"Business Rule Violation","detail":"{{detail}}","status":400}""", HttpStatusCode.BadRequest);

    /// <summary>Serves the signed-in employee's balances, history and options from fields a test can change mid-flight.</summary>
    private void ServeOwnLeave()
    {
        _api.On(HttpMethod.Get, BalancesPath(EmployeeId), () => Json(_balancesJson))
            .On(HttpMethod.Get, RequestsPath(EmployeeId), () => Json(_requestsJson))
            .On(HttpMethod.Get, OptionsPath, () => Json(_optionsJson));
    }

    private IRenderedComponent<MyLeave> RenderPage()
    {
        var cut = Render<MyLeave>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private IRenderedComponent<MyLeave> RenderAsLinkedEmployee()
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        ServeOwnLeave();
        return RenderPage();
    }

    private static IElement SubmitButton(IRenderedComponent<MyLeave> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Submit Request");

    private string? PostedBody() =>
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)];

    private JsonElement PostedJson() => JsonDocument.Parse(PostedBody()!).RootElement;

    private static string Limit(IRenderedComponent<MyLeave> cut) => cut.Find("[data-leave-limit]").TextContent.Trim();

    private static IRenderedComponent<InputFile> FileInput(IRenderedComponent<MyLeave> cut, string attribute, string? value = null) =>
        cut.FindComponents<InputFile>().Single(c =>
            c.Instance.AdditionalAttributes is { } attributes && attributes.TryGetValue(attribute, out var v)
            && (value is null || v?.ToString() == value));

    private static void ChooseDocument(IRenderedComponent<MyLeave> cut, byte[] content, string fileName, string contentType) =>
        FileInput(cut, "data-leave-document").UploadFiles(InputFileContent.CreateFromBinary(content, fileName, contentType: contentType));

    private static readonly byte[] SmallPdf = Encoding.ASCII.GetBytes("%PDF-1.7 solo parent id");

    private List<HttpRequestMessage> DocumentUploads =>
        _api.Requests.Where(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath.EndsWith("/document")).ToList();

    [Fact]
    public void OnlyTheSignedInEmployeesOwnBalancesHistoryAndFilingOptions_AreRequested()
    {
        _optionsJson = "[]";
        var cut = RenderAsLinkedEmployee();

        cut.Markup.Should().Contain("No leave balances found.").And.Contain("No leave requests yet.");
        _api.Requests.Select(r => r.RequestUri!.PathAndQuery)
            .Should().BeEquivalentTo([BalancesPath(EmployeeId), RequestsPath(EmployeeId), OptionsPath]);
    }

    [Fact]
    public void BalancesAndHistory_AreShown_AndTheChosenTypeSaysWhatIsLeft()
    {
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        _requestsJson = Paged(Request("2026-08-03", "2026-08-05", 3, "Approved"), Request("2026-09-21", "2026-09-21", 1, "Pending"));

        var cut = RenderAsLinkedEmployee();

        var balanceCells = cut.FindAll("table")[0].QuerySelectorAll("tbody td").Select(td => td.TextContent.Trim());
        balanceCells.Should().Equal("Vacation Leave", "15", "3", "12");

        cut.Find($"#leaveType option[value='{VacationTypeId}']").TextContent.Should().Be("Vacation Leave");
        cut.FindAll("[data-leave-limit]").Should().BeEmpty("nothing is chosen yet");
        cut.Find("#leaveType").Change(VacationTypeId.ToString());
        Limit(cut).Should().Be("12 days left this year");

        var history = cut.FindAll("table")[1].QuerySelectorAll("tbody tr")
            .Select(tr => string.Join("|", tr.QuerySelectorAll("td").Take(5).Select(td => td.TextContent.Trim())));
        history.Should().Equal("Vacation Leave|2026-08-03|2026-08-05|3|Approved", "Vacation Leave|2026-09-21|2026-09-21|1|Pending");
    }

    [Fact]
    public void TheTypePicker_OffersWhatTheEmployeeCanFile_NotEveryTypeTheyHaveABalanceFor()
    {
        // A balance row can exist for a type the employee may no longer file (and a statutory type
        // they may file has no balance row at all), so the picker comes from the filing options.
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        _optionsJson = $"[{SoloParentOption()},{PaternityOption()}]";

        var cut = RenderAsLinkedEmployee();

        cut.FindAll("#leaveType option").Select(o => o.TextContent.Trim())
            .Should().Equal("-- Select leave type --", "Solo Parent Leave", "Paternity Leave");
    }

    [Theory]
    [InlineData("SPL", "7 days left this year")]
    [InlineData("SPL-half", "2.5 days left this year")]
    [InlineData("PL", "Up to 7 days each time")]
    [InlineData("PL-capped", "Up to 7 days each time (1 of 4 used)")]
    public void TheChosenType_SaysHowMuchCanBeTaken(string which, string expected)
    {
        var (option, id) = which switch
        {
            "SPL" => (SoloParentOption(7m), SoloParentTypeId),
            "SPL-half" => (SoloParentOption(2.5m), SoloParentTypeId),
            "PL" => (PaternityOption(), PaternityTypeId),
            _ => (PaternityOption(maxEvents: 4, eventsUsed: 1), PaternityTypeId),
        };
        _optionsJson = $"[{option}]";
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(id.ToString());

        Limit(cut).Should().Be(expected);
        cut.FindAll("[data-maternity-case]").Should().BeEmpty("only maternity leave has a case");
    }

    [Fact]
    public void MaternityLeave_AsksForTheCase_AndWorksOutTheLimitLive()
    {
        _optionsJson = $"[{MaternityOption()}]";
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(MaternityTypeId.ToString());

        cut.FindAll("[data-maternity-case] option").Select(o => o.TextContent.Trim())
            .Should().Equal("Live birth", "Miscarriage or emergency termination");
        Limit(cut).Should().Be("Up to 105 days");

        cut.Find("[data-father-days]").Input("7");
        Limit(cut).Should().Be("Up to 98 days", "days given to the father come off the mother's leave");

        cut.Find("[data-maternity-case]").Change("MiscarriageOrEmergencyTermination");
        Limit(cut).Should().Be("Up to 60 days");
        cut.FindAll("[data-father-days]").Should().BeEmpty("only a live birth's days can be shared with the father");
    }

    [Fact]
    public void AQualifiedSoloParent_Gets15MoreDaysOfMaternityLeave_ForALiveBirthOnly()
    {
        _optionsJson = $"[{MaternityOption(bonus: true)}]";
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(MaternityTypeId.ToString());
        Limit(cut).Should().Be("Up to 120 days");

        cut.Find("[data-father-days]").Input("3");
        Limit(cut).Should().Be("Up to 117 days");

        cut.Find("[data-maternity-case]").Change("MiscarriageOrEmergencyTermination");
        Limit(cut).Should().Be("Up to 60 days");
    }

    [Fact]
    public void AMaternityRequest_SendsTheCaseAndTheFathersDays()
    {
        _optionsJson = $"[{MaternityOption()}]";
        _api.On(HttpMethod.Post, "/api/leave-requests", () =>
            Json(Request("2026-10-05", "2026-12-12", 99, "Pending", NewRequestId, MaternityTypeId, "Maternity Leave"), HttpStatusCode.Created));
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(MaternityTypeId.ToString());
        cut.Find("[data-father-days]").Input("7");
        cut.Find("#leaveFrom").Input("2026-10-05");
        cut.Find("#leaveTo").Input("2027-01-10");
        SubmitButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Leave request submitted successfully."));
        var body = PostedJson();
        body.GetProperty("maternityCase").GetString().Should().Be("LiveBirth");
        body.GetProperty("daysAllocatedToFather").GetInt32().Should().Be(7);
    }

    [Fact]
    public void AMiscarriage_SendsNoDaysToTheFather_EvenIfSomeWereEnteredFirst()
    {
        _optionsJson = $"[{MaternityOption()}]";
        _api.On(HttpMethod.Post, "/api/leave-requests", () =>
            Json(Request("2026-10-05", "2026-12-03", 60, "Pending", NewRequestId, MaternityTypeId, "Maternity Leave"), HttpStatusCode.Created));
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(MaternityTypeId.ToString());
        cut.Find("[data-father-days]").Input("5");
        cut.Find("[data-maternity-case]").Change("MiscarriageOrEmergencyTermination");
        SubmitButton(cut).Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post));
        var body = PostedJson();
        body.GetProperty("maternityCase").GetString().Should().Be("MiscarriageOrEmergencyTermination");
        body.GetProperty("daysAllocatedToFather").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("8")]
    [InlineData("-1")]
    public void FathersDaysOutsideZeroToSeven_AreRefusedWithoutCallingTheApi(string days)
    {
        _optionsJson = $"[{MaternityOption()}]";
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(MaternityTypeId.ToString());
        cut.Find("[data-father-days]").Input(days);
        SubmitButton(cut).Click();

        cut.Find("[role=alert]").TextContent.Should().Contain("Up to 7 days can be allocated to the father, for a live birth only.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void ATypeThatNeedsADocument_AsksForOne_AndCannotBeSubmittedWithoutIt()
    {
        _optionsJson = $"[{VacationOption(12)},{SoloParentOption()}]";
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(VacationTypeId.ToString());
        cut.FindAll("[data-leave-document]").Should().BeEmpty();
        SubmitButton(cut).HasAttribute("disabled").Should().BeFalse();

        cut.Find("#leaveType").Change(SoloParentTypeId.ToString());
        var input = cut.Find("[data-leave-document]");
        input.GetAttribute("accept").Should().Be(".pdf,.jpg,.jpeg,.png");
        input.HasAttribute("required").Should().BeTrue();
        SubmitButton(cut).HasAttribute("disabled").Should().BeTrue();

        ChooseDocument(cut, SmallPdf, "solo-parent-id.pdf", "application/pdf");

        cut.WaitForAssertion(() => SubmitButton(cut).HasAttribute("disabled").Should().BeFalse());
        cut.Find("[data-chosen-document]").TextContent.Should().Contain("solo-parent-id.pdf");
    }

    [Theory]
    [InlineData("oversized.pdf", "application/pdf", 10 * 1024 * 1024 + 1)]
    [InlineData("id.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 100)]
    [InlineData("id.gif", "image/gif", 100)]
    public void AFileTooLargeOrOfTheWrongType_IsRefusedBeforeAnythingIsSent(string name, string contentType, int size)
    {
        // The API cuts an oversized upload off mid-send, which a browser reports as a network
        // error rather than the reason, so the page checks the file itself first.
        _optionsJson = $"[{SoloParentOption()}]";
        var cut = RenderAsLinkedEmployee();
        cut.Find("#leaveType").Change(SoloParentTypeId.ToString());

        ChooseDocument(cut, new byte[size], name, contentType);

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain(FileRefusal));
        SubmitButton(cut).HasAttribute("disabled").Should().BeTrue();
        cut.FindAll("[data-chosen-document]").Should().BeEmpty();
    }

    [Fact]
    public void AFiledRequestWithADocument_UploadsTheDocumentToTheNewRequest()
    {
        _optionsJson = $"[{SoloParentOption()}]";
        _api.On(HttpMethod.Post, "/api/leave-requests", () =>
                Json(Request("2026-10-05", "2026-10-05", 1, "Pending", NewRequestId, SoloParentTypeId, "Solo Parent Leave"), HttpStatusCode.Created))
            .On(HttpMethod.Put, DocumentPath(NewRequestId), () =>
                Json(Request("2026-10-05", "2026-10-05", 1, "Pending", NewRequestId, SoloParentTypeId, "Solo Parent Leave", true, "solo-parent-id.pdf")));
        var cut = RenderAsLinkedEmployee();
        cut.Find("#leaveType").Change(SoloParentTypeId.ToString());
        ChooseDocument(cut, SmallPdf, "solo-parent-id.pdf", "application/pdf");
        cut.WaitForAssertion(() => SubmitButton(cut).HasAttribute("disabled").Should().BeFalse());

        SubmitButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Leave request submitted successfully."));
        // Filing stays JSON; the document follows it to the new request.
        PostedJson().GetProperty("leaveTypeId").GetGuid().Should().Be(SoloParentTypeId);
        var upload = DocumentUploads.Should().ContainSingle().Subject;
        upload.RequestUri!.AbsolutePath.Should().Be(DocumentPath(NewRequestId));
        var body = _api.RequestBodies[_api.Requests.IndexOf(upload)]!;
        body.Should().Contain("name=file").And.Contain("filename=solo-parent-id.pdf")
            .And.Contain("Content-Type: application/pdf").And.Contain("%PDF-1.7 solo parent id");
    }

    [Fact]
    public void AnUploadTheApiRefuses_LeavesTheRequestFiled_AndSaysToAttachItAgain()
    {
        _optionsJson = $"[{SoloParentOption()}]";
        _api.On(HttpMethod.Post, "/api/leave-requests", () =>
            {
                _requestsJson = Paged(Request("2026-10-05", "2026-10-05", 1, "Pending", NewRequestId, SoloParentTypeId, "Solo Parent Leave"));
                return Json(Request("2026-10-05", "2026-10-05", 1, "Pending", NewRequestId, SoloParentTypeId, "Solo Parent Leave"), HttpStatusCode.Created);
            })
            .On(HttpMethod.Put, DocumentPath(NewRequestId), () => Refusal("Only a pending request's document can be replaced."));
        var cut = RenderAsLinkedEmployee();
        cut.Find("#leaveType").Change(SoloParentTypeId.ToString());
        ChooseDocument(cut, SmallPdf, "solo-parent-id.pdf", "application/pdf");
        cut.WaitForAssertion(() => SubmitButton(cut).HasAttribute("disabled").Should().BeFalse());

        SubmitButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[data-upload-warning]").TextContent.Trim().Should().Be(
            "Your request was filed, but the document didn't upload: Only a pending request's document can be replaced. Attach it again from the request."));
        cut.Markup.Should().NotContain("Leave request submitted successfully.");
        _api.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
        cut.WaitForAssertion(() => cut.Find($"[data-attach-document='{NewRequestId}']"));
    }

    [Fact]
    public void AnUploadCutOffMidSend_IsExplainedAsAFileTooLarge()
    {
        // A body over the API's cap: Kestrel closes the connection, and the browser sees a network error.
        _optionsJson = $"[{SoloParentOption()}]";
        _api.On(HttpMethod.Post, "/api/leave-requests", () =>
                Json(Request("2026-10-05", "2026-10-05", 1, "Pending", NewRequestId, SoloParentTypeId, "Solo Parent Leave"), HttpStatusCode.Created))
            .On(HttpMethod.Put, DocumentPath(NewRequestId), () => throw new HttpRequestException("The connection was reset."));
        var cut = RenderAsLinkedEmployee();
        cut.Find("#leaveType").Change(SoloParentTypeId.ToString());
        ChooseDocument(cut, SmallPdf, "solo-parent-id.pdf", "application/pdf");
        cut.WaitForAssertion(() => SubmitButton(cut).HasAttribute("disabled").Should().BeFalse());

        SubmitButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[data-upload-warning]").TextContent.Trim().Should().Be(
            "Your request was filed, but the document didn't upload: Attach a PDF, JPG or PNG of at most 10 MB. Attach it again from the request."));
    }

    [Fact]
    public void APendingRequestOfATypeThatNeedsADocument_OffersToAttachOrReplaceIt()
    {
        var replacedId = Guid.NewGuid();
        _optionsJson = $"[{VacationOption(12)},{SoloParentOption()}]";
        _requestsJson = Paged(
            Request("2026-10-05", "2026-10-05", 1, "Pending", PendingRequestId, SoloParentTypeId, "Solo Parent Leave"),
            Request("2026-10-12", "2026-10-12", 1, "Pending", replacedId, SoloParentTypeId, "Solo Parent Leave", true, "id.png"),
            Request("2026-09-01", "2026-09-01", 1, "Approved", Guid.NewGuid(), SoloParentTypeId, "Solo Parent Leave", true, "old.pdf"),
            Request("2026-10-20", "2026-10-21", 2, "Pending"));

        var cut = RenderAsLinkedEmployee();

        cut.Find($"[data-attach-document='{PendingRequestId}']").ParentElement!.TextContent.Should().Contain("Attach document");
        cut.Find($"[data-attach-document='{replacedId}']").ParentElement!.TextContent.Should().Contain("Replace document");
        cut.FindAll("[data-attach-document]").Should().HaveCount(2, "a decided request and a type that needs no document offer nothing");
    }

    [Fact]
    public void AttachingADocumentToAPendingRequest_UploadsIt_AndRefreshesTheHistory()
    {
        _optionsJson = $"[{SoloParentOption()}]";
        _requestsJson = Paged(Request("2026-10-05", "2026-10-05", 1, "Pending", PendingRequestId, SoloParentTypeId, "Solo Parent Leave"));
        _api.On(HttpMethod.Put, DocumentPath(PendingRequestId), () =>
        {
            _requestsJson = Paged(Request("2026-10-05", "2026-10-05", 1, "Pending", PendingRequestId, SoloParentTypeId, "Solo Parent Leave", true, "id.jpg"));
            return Json(Request("2026-10-05", "2026-10-05", 1, "Pending", PendingRequestId, SoloParentTypeId, "Solo Parent Leave", true, "id.jpg"));
        });
        var cut = RenderAsLinkedEmployee();

        FileInput(cut, "data-attach-document", PendingRequestId.ToString())
            .UploadFiles(InputFileContent.CreateFromBinary([0xFF, 0xD8, 0xFF], "id.jpg", contentType: "image/jpeg"));

        cut.WaitForAssertion(() =>
            cut.Find($"[data-attach-document='{PendingRequestId}']").ParentElement!.TextContent.Should().Contain("Replace document"));
        DocumentUploads.Should().ContainSingle();
        _api.RequestBodies[_api.Requests.IndexOf(DocumentUploads[0])].Should().Contain("Content-Type: image/jpeg");
        cut.Find("[data-document-message]").TextContent.Should().Contain("id.jpg");
    }

    [Fact]
    public void AnAttachmentTheApiRefuses_ShowsTheApisReason()
    {
        _optionsJson = $"[{SoloParentOption()}]";
        _requestsJson = Paged(Request("2026-10-05", "2026-10-05", 1, "Pending", PendingRequestId, SoloParentTypeId, "Solo Parent Leave"));
        _api.On(HttpMethod.Put, DocumentPath(PendingRequestId), () => Refusal("Only a pending request's document can be replaced."));
        var cut = RenderAsLinkedEmployee();

        FileInput(cut, "data-attach-document", PendingRequestId.ToString())
            .UploadFiles(InputFileContent.CreateFromBinary(SmallPdf, "id.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => cut.Find("[data-document-error]").TextContent.Should().Contain("Only a pending request's document can be replaced."));
    }

    [Fact]
    public void AnAttachmentTooLarge_IsRefusedBeforeAnythingIsSent()
    {
        _optionsJson = $"[{SoloParentOption()}]";
        _requestsJson = Paged(Request("2026-10-05", "2026-10-05", 1, "Pending", PendingRequestId, SoloParentTypeId, "Solo Parent Leave"));
        var cut = RenderAsLinkedEmployee();

        FileInput(cut, "data-attach-document", PendingRequestId.ToString())
            .UploadFiles(InputFileContent.CreateFromBinary(new byte[10 * 1024 * 1024 + 1], "big.pdf", contentType: "application/pdf"));

        cut.WaitForAssertion(() => cut.Find("[data-document-error]").TextContent.Should().Contain(FileRefusal));
        DocumentUploads.Should().BeEmpty();
    }

    [Fact]
    public void SubmittingWithoutALeaveType_IsRefusedWithoutCallingTheApi()
    {
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        var cut = RenderAsLinkedEmployee();

        SubmitButton(cut).Click();

        cut.Find("[role=alert]").TextContent.Should().Contain("Please select a leave type.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Theory]
    [InlineData("Family trip to Baguio", "\"Family trip to Baguio\"")]
    [InlineData("   ", "null")]
    public void AValidRequest_IsFiledForTheSignedInEmployee_AndTheBalancesHistoryAndOptionsRefresh(string reason, string expectedReasonJson)
    {
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        _api.On(HttpMethod.Post, "/api/leave-requests", () =>
        {
            // What the server would now report: the days are reserved and the request is on file.
            _balancesJson = $"[{VacationBalance(total: 15, used: 6)}]";
            _optionsJson = $"[{VacationOption(daysLeft: 9)}]";
            _requestsJson = Paged(Request("2026-10-05", "2026-10-07", 3, "Pending"));
            return Json(Request("2026-10-05", "2026-10-07", 3, "Pending"), HttpStatusCode.Created);
        });
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(VacationTypeId.ToString());
        cut.Find("#leaveFrom").Input("2026-10-05");
        cut.Find("#leaveTo").Input("2026-10-07");
        cut.Find("#leaveReason").Input(reason);
        SubmitButton(cut).Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Leave request submitted successfully."));
        PostedBody().Should().Be(
            $$"""{"employeeId":"{{EmployeeId}}","leaveTypeId":"{{VacationTypeId}}","startDate":"2026-10-05","endDate":"2026-10-07","reason":{{expectedReasonJson}},"maternityCase":null,"daysAllocatedToFather":0}""");
        DocumentUploads.Should().BeEmpty("no document was chosen");
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("table")[0].QuerySelectorAll("tbody td")[3].TextContent.Trim().Should().Be("9");
            cut.FindAll("table")[1].QuerySelectorAll("tbody tr").Should().ContainSingle();
        });
    }

    [Fact]
    public void ARejectedRequest_ShowsTheApisReasonInline_AndLetsTheEmployeeTryAgain()
    {
        _api.On(HttpMethod.Post, "/api/leave-requests", () => Refusal("Insufficient leave balance. Available: 1, Requested: 3."));
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(VacationTypeId.ToString());
        SubmitButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Trim()
            .Should().Be("Insufficient leave balance. Available: 1, Requested: 3."));
        cut.Markup.Should().NotContain("Leave request submitted successfully.");
        SubmitButton(cut).HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void WhileARequestIsBeingFiled_SubmitIsDisabled_AndASecondClickFilesNothing()
    {
        var gate = _api.OnGated(HttpMethod.Post, "/api/leave-requests");
        var cut = RenderAsLinkedEmployee();
        cut.Find("#leaveType").Change(VacationTypeId.ToString());

        SubmitButton(cut).Click();

        cut.WaitForAssertion(() => SubmitButton(cut).HasAttribute("disabled").Should().BeTrue());
        SubmitButton(cut).Click();
        _api.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);

        gate.SetResult(Json(Request("2026-10-05", "2026-10-05", 1, "Pending"), HttpStatusCode.Created));
        cut.WaitForAssertion(() => SubmitButton(cut).HasAttribute("disabled").Should().BeFalse());
        _api.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
    }

    [Fact]
    public void AnAccountWithoutAnEmployeeLink_IsToldSo_AndCannotFileLeave()
    {
        // Without an employee id the form would file a request against Guid.Empty.
        var cut = Render<MyLeave>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Your account is not linked to an employee record."));
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Submit Request");
        cut.FindAll(".animate-spin").Should().BeEmpty();
        _api.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AnEndDateBeforeTheStartDate_IsRefusedWithoutCallingTheApi()
    {
        _balancesJson = $"[{VacationBalance(total: 15, used: 3)}]";
        var cut = RenderAsLinkedEmployee();

        cut.Find("#leaveType").Change(VacationTypeId.ToString());
        cut.Find("#leaveFrom").Input("2026-10-07");
        cut.Find("#leaveTo").Input("2026-10-05");
        SubmitButton(cut).Click();

        cut.Find("[role=alert]").TextContent.Should().Contain("The end date cannot be before the start date.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void BalancesOrHistoryThatFailToLoad_AreReported_InsteadOfCrashingThePage()
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        _api.On(HttpMethod.Get, BalancesPath(EmployeeId), HttpStatusCode.InternalServerError)
            .On(HttpMethod.Get, RequestsPath(EmployeeId), () => Json(_requestsJson))
            .On(HttpMethod.Get, OptionsPath, () => Json(_optionsJson));

        var cut = Render<MyLeave>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load your leave"));
        cut.FindAll(".animate-spin").Should().BeEmpty();
    }

    [Fact]
    public void FilingOptionsThatFailToLoad_AreReported()
    {
        _auth.SetClaims(new Claim("employee_id", EmployeeId.ToString()));
        _api.On(HttpMethod.Get, BalancesPath(EmployeeId), () => Json(_balancesJson))
            .On(HttpMethod.Get, RequestsPath(EmployeeId), () => Json(_requestsJson))
            .On(HttpMethod.Get, OptionsPath, () => Json("""{"detail":"Leave types are unavailable."}""", HttpStatusCode.InternalServerError));

        var cut = Render<MyLeave>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Leave types are unavailable."));
    }
}
