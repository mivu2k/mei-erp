using MeiErp.Modules.Hr;

namespace MeiErp.Host.Services;

public static class KioskEndpoints
{
    private const int MaxFrameBytes = 512 * 1024;

    public static IEndpointRouteBuilder MapKioskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/hr/kiosk/{token}/frame", async (
            string token, HttpRequest request, IKioskService kiosk,
            IQrFrameDecoder decoder, CancellationToken ct) =>
        {
            if (await kiosk.ResolveStationAsync(token, ct) is null || request.ContentLength is > MaxFrameBytes)
                return Results.NoContent();
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length is 0 or > MaxFrameBytes) return Results.NoContent();
            var text = decoder.Decode(buffer.ToArray());
            return text is null ? Results.NoContent() : Results.Ok(new { text });
        }).AllowAnonymous().DisableAntiforgery();
        return app;
    }
}
