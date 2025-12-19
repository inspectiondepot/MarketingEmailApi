namespace EmailCampaign.Services;

using EmailCampaign.Models;

public interface ICampaignService
{
    Task<object> StartCampaignAsync(CampaignRequest request, CampaignModelRequest model);
}
