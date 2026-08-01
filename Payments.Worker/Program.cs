using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Payments.Worker;

var builder = Host.CreateApplicationBuilder(args);

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
    // authenticates your Azure user through the browser.
    serviceBusCredential = new InteractiveBrowserCredential(
        new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId
        });
}
else
{
    // Azure:
    // uses the system-assigned identity of the worker's host.
    serviceBusCredential =
        new ManagedIdentityCredential(
            ManagedIdentityId.SystemAssigned);
}

builder.Services.AddSingleton(_ =>
    new ServiceBusClient(
        fullyQualifiedNamespace,
        serviceBusCredential));

builder.Services.AddSingleton(serviceProvider =>
{
    var serviceBusClient =
        serviceProvider.GetRequiredService<ServiceBusClient>();

    return serviceBusClient.CreateProcessor(
        queueName,
        new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 1
        });
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

await host.RunAsync();