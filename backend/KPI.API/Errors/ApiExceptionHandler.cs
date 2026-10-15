using KPI.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Errors;

/// <summary>
/// One place that turns exceptions into RFC 7807 problem responses with the right status code.
/// Unexpected errors are logged and returned without internal details.
/// </summary>
public class ApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, "Forbidden", e.Message),
            InvalidStateException e => (StatusCodes.Status409Conflict, "Conflict", e.Message),
            RequestValidationException e => (StatusCodes.Status400BadRequest, "Invalid request", e.Message),
            TooManyRequestsException e => (StatusCodes.Status429TooManyRequests, "Too many requests", e.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict",
                "This record was changed by someone else. Reload and try again."),
            _ => (StatusCodes.Status500InternalServerError, "Server error",
                "An unexpected error occurred."),
        };

        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception on {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        if (exception is TooManyRequestsException tooMany)
            httpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(tooMany.RetryAfter.TotalSeconds)).ToString();
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            // Kept alongside RFC 7807 fields so existing clients that read `message` keep working.
            Extensions = { ["message"] = detail },
        }, cancellationToken);
        return true;
    }
}
