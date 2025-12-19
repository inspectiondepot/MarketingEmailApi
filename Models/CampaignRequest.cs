namespace EmailCampaign.Models;

public class CampaignRequest
{
    public string Bucket { get; set; } = "";
    public string Key { get; set; } = "";
    public string Campaign { get; set; } = "";
}

public class CampaignModelRequest
{
    public string TemplateName { get; set; }   // e.g., "inspectiondepot.html"
    public string FromEmail { get; set; }      // e.g., "noreply@domain.com"
    public string Subject { get; set; }        // e.g., "Monthly Update"
}
