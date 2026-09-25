using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using PeopleCore.API.Filters;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A form over the action's limits fails to read. Left to model binding, that failure comes back as
/// a "Failed to read the request form" validation problem; this filter reads the form first and
/// turns the failure into a <see cref="DomainException"/> carrying the action's own message, which
/// the exception middleware returns like any other refusal (400, <c>detail</c> = the message).
/// </summary>
public class RefuseOversizedFormAttributeTests
{
    private const string Message = "Attach a PDF, JPG or PNG of at most 10 MB.";
    private readonly RefuseOversizedFormAttribute _sut = new(Message);

    private static (ResourceExecutingContext Context, Func<bool> NextCalled, ResourceExecutionDelegate Next) ContextFor(HttpContext http)
    {
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var called = false;
        return (
            new ResourceExecutingContext(action, filters, new List<IValueProviderFactory>()),
            () => called,
            () => { called = true; return Task.FromResult(new ResourceExecutedContext(action, filters)); });
    }

    private static async Task<DefaultHttpContext> MultipartRequest(int fileBytes, long? multipartLimit = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[fileBytes]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "a.pdf");

        var http = new DefaultHttpContext();
        var body = new MemoryStream();
        await content.CopyToAsync(body);
        body.Position = 0;
        http.Request.Body = body;
        http.Request.ContentLength = body.Length;
        http.Request.ContentType = content.Headers.ContentType!.ToString();
        if (multipartLimit is { } limit)
            http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions { MultipartBodyLengthLimit = limit }));
        return http;
    }

    [Fact]
    public async Task AFormOverTheMultipartLimit_IsRefusedWithTheActionsMessage()
    {
        var http = await MultipartRequest(fileBytes: 200, multipartLimit: 100);
        var (context, nextCalled, next) = ContextFor(http);

        var act = () => _sut.OnResourceExecutionAsync(context, next);

        await act.Should().ThrowAsync<DomainException>().WithMessage(Message);
        nextCalled().Should().BeFalse();
    }

    [Fact]
    public async Task ABodyOverTheRequestSizeLimit_IsRefusedWithTheActionsMessage()
    {
        // What Kestrel throws on reading past [RequestSizeLimit].
        var http = await MultipartRequest(fileBytes: 10);
        http.Request.Body = new ThrowingStream(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));
        var (context, nextCalled, next) = ContextFor(http);

        var act = () => _sut.OnResourceExecutionAsync(context, next);

        await act.Should().ThrowAsync<DomainException>().WithMessage(Message);
        nextCalled().Should().BeFalse();
    }

    [Fact]
    public async Task AnyOtherReadFailure_IsLeftToModelBinding()
    {
        // A malformed or abandoned body is not "your file is too big" - and must not escape as a
        // 500 either. The filter passes on; the request remembers the failed read, so model binding
        // meets the same failure and answers with its usual 400 validation problem.
        var http = await MultipartRequest(fileBytes: 10);
        http.Request.Body = new ThrowingStream(new IOException("Unexpected end of Stream."));
        var (context, nextCalled, next) = ContextFor(http);

        await _sut.OnResourceExecutionAsync(context, next);

        nextCalled().Should().BeTrue();
        var modelBindingRead = () => http.Request.ReadFormAsync();
        await modelBindingRead.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task AFormWithinTheLimits_IsReadOnce_AndPassedOn()
    {
        var http = await MultipartRequest(fileBytes: 50, multipartLimit: 100);
        var (context, nextCalled, next) = ContextFor(http);

        await _sut.OnResourceExecutionAsync(context, next);

        nextCalled().Should().BeTrue();
        http.Request.Form.Files.Should().ContainSingle().Which.Length.Should().Be(50);
    }

    [Fact]
    public async Task ARequestThatIsNotAForm_IsPassedOn()
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = "application/json";
        var (context, nextCalled, next) = ContextFor(http);

        await _sut.OnResourceExecutionAsync(context, next);

        nextCalled().Should().BeTrue();
    }

    [Fact]
    public void RunsAfterTheLimitFilters_SoTheyApplyToItsRead()
    {
        _sut.Order.Should().BeGreaterThan(new RequestFormLimitsAttribute().Order);
        _sut.Order.Should().BeGreaterThan(new RequestSizeLimitAttribute(1).Order);
    }

    private sealed class ThrowingStream(Exception toThrow) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw toThrow;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => throw toThrow;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => throw toThrow;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
