using System.Text.Json;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";
            var problem = new { title = "Business Rule Violation", detail = ex.Message, status = 400 };
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
        catch (KeyNotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/problem+json";
            var problem = new { title = "Not Found", detail = ex.Message, status = 404 };
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            var problem = new { title = "Internal Server Error", detail = "An unexpected error occurred.", status = 500 };
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
    }
}
