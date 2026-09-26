using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing=>
    {
        tracing
        .AddSource("DemoApi")
        .SetSampler(new AlwaysOnSampler()) //very important to keep span on
        .AddConsoleExporter();
    }).WithMetrics(meterProvider=>
    {
        meterProvider.AddMeter("DemoApi")
        .AddView("api.request.duration",             //view make us to apply configuration for specific generated measurment before export it 
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = new double[]
                    {
                            5,
                            10,
                            25,
                            50,
                            100,
                            250,
                            500,
                            1000
                    }
                })
        .AddConsoleExporter();
    });


var app = builder.Build();

// connect to traceProvider and MeterProvider
var activitySource = new ActivitySource("DemoApi");
var meter = new Meter("DemoApi");

//create custom metrics
var request_counter = meter.CreateCounter<long>("api.request", description: "to calaculte number of request", unit: "1");

var request_duration = meter.CreateHistogram<double>("api.request.duration", description: "Api request Duration", unit: "ms");


app.MapGet("/", () =>
{
    return "OpenTelemetry Demo API";
});

app.MapGet("/api/products", () =>
{

    var stopwatch = Stopwatch.StartNew();
    using var parentSpan = activitySource.StartActivity("Get products",ActivityKind.Server);

    using var childSpan = activitySource.StartActivity("load products", ActivityKind.Internal);

    // trying to make our attribut low cardinality 
    // cardinality is very importnan concept to be aware of it


    var tag = new TagList
    {
        {"route", "/api/products" },
        {"method", "GET" },
        {"http.status_code", "200" }
    };


    var products = new[]
    {
        new { Id = 1, Name = "Laptop", Price = 1200 },
        new { Id = 2, Name = "Phone", Price = 800 },
        new { Id = 3, Name = "Headphones", Price = 150 }
    };

    
    stopwatch.Stop();

    request_counter.Add(1,tag);

    parentSpan?.SetStatus(Status.Ok);

    request_duration.Record(stopwatch.Elapsed.TotalMilliseconds, tag);

    return Results.Ok(products);
});

app.MapGet("/api/products/{id:int}", (int id) =>
{
    
    using var span = activitySource.StartActivity("get product by id",ActivityKind.Internal,StatusCode.Ok.ToString());
    span?.SetTag("http.status_code", 200);
    span?.SetTag("http_route", $"api/products/{id}");

    if (id <= 0)
    {
        span?.SetTag("http.status_code", 404);
        span?.SetStatus(Status.Error);
        span?.AddEvent(new ActivityEvent($"not found product with id: {id}"));
        throw new Exception("failed to load product");
       
    }
        
        var product = new
        {
            Id = id,
            Name = $"Product {id}",
            Price = 100
        };


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