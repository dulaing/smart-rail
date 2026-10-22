namespace SmartRail.Api.Modules.Catering.Callbacks;

// The route Grilled calls with status changes. It takes no user token: the signature guards it. It
// fails closed when the secret is not configured and never logs a body, signature or pickup code.
public static class CallbackEndpoints
{
    public const string Path = "/internal/catering/order-updates";
    public const string SecretSetting = "Grilled:CallbackSecret";
    public const int MaxBodyBytes = 64 * 1024;

    public static void MapCallbacks(this IEndpointRouteBuilder app)
    {
        app.MapPost(Path, ReceiveUpdate).AllowAnonymous();
    }

    private static async Task<IResult> ReceiveUpdate(
        HttpContext context, IConfiguration configuration, TimeProvider time, OrderUpdateReceiver receiver, ILogger<OrderUpdateReceiver> logger)
    {
        // Bounded, so an unauthenticated caller cannot make the API buffer an unlimited body.
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE", "The callback body is too large.");
        }

        var body = await ReadBoundedAsync(context.Request.Body, context.RequestAborted);
        if (body is null)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE", "The callback body is too large.");
        }

        // Authenticate on the raw bytes before looking at what the callback says or whether it is a repeat.
        var problem = CallbackSignature.Check(
            configuration[SecretSetting],
            context.Request.Headers[CallbackSignature.TimestampHeader].ToString(),
            context.Request.Headers[CallbackSignature.SignatureHeader].ToString(),
            body,
            time.GetUtcNow());
        if (problem != SignatureProblem.None)
        {
            CateringMetrics.CallbacksRejected.Add(1, new KeyValuePair<string, object?>("reason", problem.ToString()));
            logger.LogWarning("Rejected a Grilled callback: {Reason}.", problem);
            return Problem(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "The callback signature or timestamp is not valid.");
        }

        var result = await receiver.ReceiveAsync(body, context.RequestAborted);
        CateringMetrics.CallbacksReceived.Add(1, new KeyValuePair<string, object?>("result", result.GetType().Name));
        return result switch
        {
            ReceiveResult.Applied applied => Results.Ok(new { result = applied.Result }),
            ReceiveResult.Duplicate => Results.Ok(new { result = "DUPLICATE" }),
            ReceiveResult.Conflict => Problem(StatusCodes.Status409Conflict, "EVENT_CONFLICT", "This event ID was already received with different content."),
            ReceiveResult.NotReady => Problem(StatusCodes.Status503ServiceUnavailable, "ORDER_NOT_RECONCILED", "The order's details are not available yet. Send the callback again."),
            ReceiveResult.UnknownLink => Problem(StatusCodes.Status404NotFound, "UNKNOWN_ORDER_LINK", "No order link has this ID."),
            ReceiveResult.Invalid invalid => Problem(StatusCodes.Status400BadRequest, "INVALID_REQUEST", invalid.Detail),
            _ => Problem(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Unexpected result."),
        };
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(title: code, detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
