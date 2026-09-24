using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.API.Filters;

/// <summary>
/// Refuses a form that is over the action's limits with the action's own message.
/// </summary>
/// <remarks>
/// <para>
/// Past <see cref="RequestSizeLimitAttribute"/> or <see cref="RequestFormLimitsAttribute"/>, reading
/// the form fails. Model binding would catch that failure and answer with a 400 "Failed to read the
/// request form" validation problem.
/// </para>
/// <para>
/// This filter reads the form first, before model binding, and throws a <see cref="DomainException"/>
/// instead. The exception middleware returns it like any other refusal: 400 with <c>detail</c> set to
/// <see cref="Message"/>. A form that reads cleanly stays cached on the request for model binding.
/// </para>
/// <para>
/// It runs after the limit filters (order 900), so their limits apply to this read.
/// </para>
/// <para>
/// A client still uploading when the server answers may never see the response. Kestrel closes the
/// connection instead of reading the rest of an over-limit body. So clients should check the size
/// before sending.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RefuseOversizedFormAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    public RefuseOversizedFormAttribute(string message) => Message = message;

    /// <summary>The refusal, as the action's service words it.</summary>
    public string Message { get; }

    public int Order => 1000;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.HasFormContentType)
        {
            try
            {
                await request.ReadFormAsync(context.HttpContext.RequestAborted);
            }
            // InvalidDataException: a form limit (the multipart body length) was exceeded.
            // BadHttpRequestException 413: the request body passed the request size limit.
            catch (Exception ex) when (ex is InvalidDataException
                                       or BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
            {
                throw new DomainException(Message);
            }
        }

        await next();
    }
}
