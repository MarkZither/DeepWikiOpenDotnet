namespace deepwiki_open_dotnet.ApiService.Middleware;

/// <summary>
/// Middleware that adds security headers to all responses for production deployment.
/// Implements OWASP security best practices.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // X-Content-Type-Options: Prevent MIME type sniffing
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

        // X-Frame-Options: Prevent clickjacking attacks
        context.Response.Headers.Append("X-Frame-Options", "DENY");

        // X-XSS-Protection: Enable browser XSS protection (legacy browsers)
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

        // Referrer-Policy: Control referrer information
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // Content-Security-Policy: Restrict resource loading (adjust based on your needs)
        // Note: For API-only services, this is less critical but still recommended
        context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'; frame-ancestors 'none'");

        // Permissions-Policy: Disable unnecessary browser features
        context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");

        // Remove server header to avoid information disclosure
        context.Response.Headers.Remove("Server");
        context.Response.Headers.Remove("X-Powered-By");

        await _next(context);
    }
}

/// <summary>
/// Extension method for easy middleware registration.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// Adds security headers middleware to the application pipeline.
    /// Should be added early in the pipeline to ensure headers are set for all responses.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
