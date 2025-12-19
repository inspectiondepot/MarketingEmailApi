using EmailCampaign.Models;
using EmailCampaign.Services;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

public class CampaignProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    private readonly ConcurrentQueue<CampaignModelRequest> _campaignQueue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CampaignRequest _campaignRequest;

    public CampaignProcessor(IOptions<CampaignRequest> options, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _campaignRequest = options.Value;
    }

    public void EnqueueCampaign(CampaignModelRequest request)
    {
        _campaignQueue.Enqueue(request);
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (_campaignQueue.TryDequeue(out var request))
            {
                try
                {
                    // Create a scope to resolve scoped services
                    using var scope = _serviceProvider.CreateScope();
                    var campaignService = scope.ServiceProvider.GetRequiredService<ICampaignService>();

                    await campaignService.StartCampaignAsync(_campaignRequest, request);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing campaign {request.TemplateName}: {ex.Message}");
                }
            }
        }
    }
}
