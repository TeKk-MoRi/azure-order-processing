using Azure.Messaging.ServiceBus;

namespace Payments.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ServiceBusProcessor _processor;

    public Worker(
        ILogger<Worker> logger,
        ServiceBusProcessor processor)
    {
        _logger = logger;
        _processor = processor;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        var processingStarted = false;

        try
        {
            await _processor.StartProcessingAsync(stoppingToken);

            processingStarted = true;

            _logger.LogInformation(
                "Payments.Worker started listening to Service Bus.");

            await WaitUntilStoppedAsync(stoppingToken);
        }
        finally
        {
            if (processingStarted)
            {
                await _processor.StopProcessingAsync(
                    CancellationToken.None);
            }

            _processor.ProcessMessageAsync -= ProcessMessageAsync;
            _processor.ProcessErrorAsync -= ProcessErrorAsync;

            _logger.LogInformation(
                "Payments.Worker stopped listening to Service Bus.");
        }
    }

    private async Task ProcessMessageAsync(
        ProcessMessageEventArgs args)
    {
        var messageBody = args.Message.Body.ToString();

        _logger.LogInformation(
            "Received OrderCreated message. " +
            "MessageId: {MessageId}, Body: {MessageBody}",
            args.Message.MessageId,
            messageBody);

        // Simulate payment processing.
        await Task.Delay(
            TimeSpan.FromSeconds(1),
            args.CancellationToken);

        _logger.LogInformation(
            "Payment processed successfully. MessageId: {MessageId}",
            args.Message.MessageId);

        await args.CompleteMessageAsync(
            args.Message,
            args.CancellationToken);
    }

    private Task ProcessErrorAsync(
        ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Service Bus error. " +
            "Namespace: {FullyQualifiedNamespace}, " +
            "Entity: {EntityPath}, " +
            "ErrorSource: {ErrorSource}",
            args.FullyQualifiedNamespace,
            args.EntityPath,
            args.ErrorSource);

        return Task.CompletedTask;
    }

    private static async Task WaitUntilStoppedAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Expected when the application is shutting down.
        }
    }
}