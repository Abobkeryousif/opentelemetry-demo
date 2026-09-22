using OpenTelemetry.Trace;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing=>
    {
        tracing
        .AddSource("DemoApi")
        .SetSampler(new AlwaysOnSampler()) //very important to keep span on
        .AddConsoleExporter();
    });


var app = builder.Build();

var activitySource = new ActivitySource("DemoApi");

app.MapGet("/", () =>
{
    return "OpenTelemetry Demo API";
});

app.MapGet("/api/products", () =>
{

    using var span = activitySource.StartActivity(
        "Get products",
        ActivityKind.Server,
        StatusCode.Ok.ToString()
        );

    span?.SetTag("http.status_code", 200);
    span?.SetTag("http.route", "/api/products");

    span?.AddEvent(new ActivityEvent("products loaded successfly"));

    var products = new[]
    {
        new { Id = 1, Name = "Laptop", Price = 1200 },
        new { Id = 2, Name = "Phone", Price = 800 },
        new { Id = 3, Name = "Headphones", Price = 150 }
    };

    return Results.Ok(products);
});

app.MapGet("/api/products/{id:int}", (int id) =>
{
    using var span = activitySource.StartActivity
    ("Get product by id", ActivityKind.Internal);




    if (id <= 0)
        throw new Exception("failed to load product");

    span?.AddEvent(new ActivityEvent("not founded any data"));

    var product = new
    {
        Id = id,
        Name = $"Product {id}",
        Price = 100
    };

    return Results.Ok(product);

});
app.MapPost("/api/orders", (OrderRequest request) =>
{
    if (request.ProductId <= 0 || request.Quantity <= 0)
        return Results.BadRequest("Invalid order");

    var order = new
    {
        OrderId = Guid.NewGuid(),
        request.ProductId,
        request.Quantity,
        Total = request.Quantity * 100
    };

    return Results.Ok(order);
});

app.MapGet("/api/orders/{id:guid}", (Guid id) =>
{
    return Results.Ok(new
    {
        OrderId = id,
        Status = "Completed"
    });
});

app.MapGet("/api/error", () =>
{
    using var span = activitySource.StartActivity(
        "Get products",
        ActivityKind.Server);

    try
    {
        throw new InvalidOperationException("Failed to load products.");
    }
    catch (Exception ex)
    {
        span?.SetStatus(ActivityStatusCode.Error, "Failed to load products");

        span?.AddEvent(new ActivityEvent(
            "exception",
            tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message }
            }));

        return Results.Problem("Failed to load products.");
    }
});

app.MapGet("/api/delay", async () =>
{
    await Task.Delay(1000);

    return Results.Ok(new
    {
        Message = "Response after 1 second"
    });
});

app.Run();

record OrderRequest(int ProductId, int Quantity);