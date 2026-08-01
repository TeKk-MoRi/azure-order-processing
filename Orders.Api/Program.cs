using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Contracts;
using Orders.Api.Data;
using Orders.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// We will enable this later.
// builder.Services.AddApplicationInsightsTelemetry();

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

var fullyQualifiedNamespace =
    builder.Configuration["ServiceBus:FullyQualifiedNamespace"]
    ?? throw new InvalidOperationException(
        "ServiceBus:FullyQualifiedNamespace is missing.");

var queueName =
    builder.Configuration["ServiceBus:QueueName"]
    ?? throw new InvalidOperationException(
        "ServiceBus:QueueName is missing.");

TokenCredential serviceBusCredential;

if (builder.Environment.IsDevelopment())
{
    var tenantId =
        builder.Configuration["Azure:TenantId"]
        ?? throw new InvalidOperationException(
            "Azure:TenantId is missing in Development.");

    // Local development:
    // opens the browser and authenticates your Azure user.
    serviceBusCredential = new InteractiveBrowserCredential(
        new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId
        });
}
else
{
    // Azure:
    // uses the system-assigned managed identity of Orders.Api.
    serviceBusCredential =
        new ManagedIdentityCredential(
            ManagedIdentityId.SystemAssigned);
}

// The DI container creates and disposes this singleton.
builder.Services.AddSingleton(_ =>
    new ServiceBusClient(
        fullyQualifiedNamespace,
        serviceBusCredential));

// The sender shares the ServiceBusClient's AMQP connection.
builder.Services.AddSingleton(serviceProvider =>
{
    var serviceBusClient =
        serviceProvider.GetRequiredService<ServiceBusClient>();

    return serviceBusClient.CreateSender(queueName);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        service = "Orders.Api",
        time = DateTime.UtcNow
    });
});

app.MapPost("/orders", async (
    CreateOrderRequest request,
    OrdersDbContext dbContext,
    ServiceBusSender serviceBusSender,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var order = Order.Create(
        request.CustomerName,
        request.Amount);

    dbContext.Orders.Add(order);

    await dbContext.SaveChangesAsync(cancellationToken);

    var message = new OrderCreatedMessage(
        order.Id,
        order.CustomerName,
        order.Amount,
        order.CreatedAtUtc);

    var serviceBusMessage =
        new ServiceBusMessage(
            BinaryData.FromObjectAsJson(message))
        {
            ContentType = "application/json",
            Subject = "OrderCreated",
            MessageId = order.Id.ToString()
        };

    await serviceBusSender.SendMessageAsync(
        serviceBusMessage,
        cancellationToken);

    logger.LogInformation(
        "OrderCreated message sent for OrderId {OrderId}",
        order.Id);

    return Results.Created(
        $"/orders/{order.Id}",
        new
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
        .FirstOrDefaultAsync(
            order => order.Id == id,
            cancellationToken);

    return order is null
        ? Results.NotFound()
        : Results.Ok(order);
});

app.Run();