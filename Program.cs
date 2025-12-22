using Amazon.S3;
using Amazon.SimpleEmailV2;
using EmailCampaign.Models;
using EmailCampaign.Services; 
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// --- AWS Services ---
builder.Services.AddAWSService<IAmazonSimpleEmailServiceV2>();
builder.Services.AddAWSService<IAmazonS3>();

// --- Custom Services ---
builder.Services.AddScoped<IEmailSenderService, EmailSenderService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddSingleton<ITemplateService, TemplateService>();

// --- Background Campaign Processor ---
builder.Services.AddSingleton<CampaignProcessor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CampaignProcessor>());

// --- Controllers & Swagger ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Campaign API", Version = "v1" });
});

// --- Configuration for default campaign settings (optional) ---
builder.Services.Configure<CampaignRequest>(
    builder.Configuration.GetSection("CampaignSettings")
);

var app = builder.Build();



// --- Health check ---
app.MapGet("/health", () => "OK");

// --- Swagger & HTTPS ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Campaign API v1");
    });
}
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
 

app.Run();
