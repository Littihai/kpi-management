namespace KPI.Domain.Exceptions;

/// <summary>Base type for expected, user-facing failures. Mapped to HTTP status codes by the API.</summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>The resource does not exist, or is outside the caller's scope (404).</summary>
public class NotFoundException(string message) : AppException(message);

/// <summary>The caller is authenticated but not allowed to perform the action (403).</summary>
public class ForbiddenException(string message) : AppException(message);

/// <summary>The action is not valid for the resource's current state (409).</summary>
public class InvalidStateException(string message) : AppException(message);

/// <summary>The request data is invalid (400).</summary>
public class RequestValidationException(string message) : AppException(message);
