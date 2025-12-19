using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Threading.Tasks;

public interface ITemplateService
{
    Task<string> GetTemplateAsync(string templateFileName);
}

public class TemplateService : ITemplateService
{
    private readonly IWebHostEnvironment _env;

    public TemplateService(IWebHostEnvironment env)
    {
        _env = env;
    }

    /// <summary>
    /// Reads an HTML template file from wwwroot/templates
    /// </summary>
    /// <param name="templateFileName">File name, e.g., "inspectiondepot.html"</param>
    public async Task<string> GetTemplateAsync(string templateFileName)
    {
        // Build full path: wwwroot/templates/{templateFileName}
        var filePath = Path.Combine(_env.WebRootPath, "templates", templateFileName);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Template file not found: {filePath}");

        return await File.ReadAllTextAsync(filePath);
    }
}
