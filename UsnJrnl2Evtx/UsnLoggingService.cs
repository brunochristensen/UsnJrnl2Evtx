namespace UsnJrnl2Evtx;

public class UsnLoggingService : BackgroundService
{
    private readonly ILogger<UsnLoggingService> _logger;

    public UsnLoggingService(ILogger<UsnLoggingService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Service starting at: {time}", DateTimeOffset.Now);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Heartbeat: service is running. Time: {time}", DateTimeOffset.Now);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in service loop");
            throw;
        }
        finally
        {
            _logger.LogInformation("Service stopping at: {time}", DateTimeOffset.Now);
        }
    }
}