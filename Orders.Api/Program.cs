using Microsoft.EntityFrameworkCore;
using Orders.Api.Contracts;
using Orders.Api.Data;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Orders.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//builder.Services.AddApplicationInsightsTelemetry();

var ordersDbConnectionString =
    builder.Configuration.GetConnectionString("OrdersDb")
    ?? throw new InvalidOperationException(
        "Connection string 'OrdersDb' was not found.");

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseSqlServer(
        ordersDbConnectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 6,
                maxRetryDelay: TimeSpan.FromSeconds(20),
                errorNumbersToAdd: null);
        }));

var serviceBusConnectionString = builder.Configuration["ServiceBus:ConnectionString"];

if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
{
    builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnectionString));
}

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        service = "Orders.Api API",
        time = DateTime.UtcNow
    });
});

app.MapPost("/orders", async (
    CreateOrderRequest request,
    OrdersDbContext dbContext,
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var order = Order.Create(request.CustomerName, request.Amount);

    dbContext.Orders.Add(order);

    await dbContext.SaveChangesAsync(cancellationToken);

    var message = new OrderCreatedMessage(
        order.Id,
        order.CustomerName,
        order.Amount,
        order.CreatedAtUtc);

    var serviceBusClient = serviceProvider.GetService<ServiceBusClient>();

    if (serviceBusClient is not null)
    {
        var queueName = configuration["ServiceBus:QueueName"];

        if (string.IsNullOrWhiteSpace(queueName))
            throw new InvalidOperationException("ServiceBus queue name is missing.");

        var sender = serviceBusClient.CreateSender(queueName);

        var serviceBusMessage = new ServiceBusMessage(JsonSerializer.Serialize(message))
        {
            ContentType = "application/json",
            Subject = "OrderCreated",
            MessageId = order.Id.ToString()
        };

        await sender.SendMessageAsync(serviceBusMessage, cancellationToken);

        logger.LogInformation("OrderCreated message sent for OrderId {OrderId}", order.Id);
    }
    else
    {
        logger.LogWarning("Service Bus is not configured. OrderCreated message was not sent.");
    }

    return Results.Created($"/orders/{order.Id}", new
    {
        order.Id,
        order.CustomerName,
        order.Amount,
        order.CreatedAtUtc
    });
});

app.MapGet("/orders/{id:guid}", async (
    Guid id,
    OrdersDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var order = await dbContext.Orders
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    return order is null
        ? Results.NotFound()
        : Results.Ok(order);
});

app.Run();