using EmailCampaign.Models;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/campaign")]
public class CampaignController : ControllerBase
{
    private readonly CampaignProcessor _processor;

    public CampaignController(CampaignProcessor processor)
    {
        _processor = processor;
    }

    [HttpPost("start")]
    public IActionResult StartCampaign([FromBody] CampaignModelRequest request)
    {
        _processor.EnqueueCampaign(request);
        return Ok(new { message = $"Campaign '{request.TemplateName}' queued." });
    }
}
