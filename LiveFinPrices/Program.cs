using System.Net.WebSockets;
using LiveFinPrices.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://localhost:5000");
        builder.Services.AddCors(opts =>
            opts.AddDefaultPolicy(p =>
                p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        builder.Services.AddSingleton<IPriceProvider, BinancePriceProvider>();
        builder.Services.AddHostedService(p =>
            (BinancePriceProvider)p.GetRequiredService<IPriceProvider>());

        builder.Services.AddSingleton<WebSocketBroadcastService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WebSocketBroadcastService>());

        var app = builder.Build();

        app.UseCors();
        app.UseWebSockets();

        var provider = app.Services.GetRequiredService<IPriceProvider>();

        app.MapGet("/api/instruments", () =>
            Results.Ok(provider.GetAvailableInstruments()));

        app.MapGet("/api/prices/{symbol}", (string symbol) =>
        {
            var price = provider.GetCurrentPrice(symbol.ToLower());
            return price > 0 ? Results.Ok(new { symbol, price }) : Results.NotFound();
        });

        app.Map("/ws", async ctx =>
        {
            if (ctx.WebSockets.IsWebSocketRequest)
            {
                var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                var svc = ctx.RequestServices.GetRequiredService<WebSocketBroadcastService>();
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                var linked = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, lifetime.ApplicationStopping);
                await svc.HandleClientAsync(ws, linked.Token);
            }
            else
            {
                ctx.Response.StatusCode = 400;
            }
        });

        app.Run();
    }
}
