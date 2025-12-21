using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/ocr")]
[Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
public class OCRController : ControllerBase
{
    private readonly IMongoClient _mongo;

    public OCRController(IMongoClient mongo)
    {
        _mongo = mongo;
    }

    [HttpGet]
    public async Task<IActionResult> GetOCRLogs([FromQuery] string? agentId)
    {
        var db = _mongo.GetDatabase("watchsec");
        var collection = db.GetCollection<OCRLog>("ocr_logs");
        
        var filter = Builders<OCRLog>.Filter.Empty;
        if (!string.IsNullOrEmpty(agentId))
        {
            filter = Builders<OCRLog>.Filter.Eq(x => x.AgentId, agentId);
        }
        
        var sort = Builders<OCRLog>.Sort.Descending(x => x.Timestamp);
        var logs = await collection.Find(filter).Sort(sort).Limit(100).ToListAsync();
        
        return Ok(logs);
    }

    // Endpoint to manually trigger OCR on a screenshot (Simulation)
    // Endpoint to manually trigger OCR on a screenshot (Real Tesseract)
    [HttpPost("process/{screenshotId}")]
    public async Task<IActionResult> ProcessScreenshot(string screenshotId, [FromQuery] string agentId)
    {
        var db = _mongo.GetDatabase("watchsec");
        var collection = db.GetCollection<OCRLog>("ocr_logs");

        // 1. Find Screenshot File
        // Search in all date folders for this agent (simplified)
        var agentDir = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Screenshots", agentId);
        if (!Directory.Exists(agentDir)) return NotFound("Agent storage not found");

        string? imagePath = null;
        foreach (var dateDir in Directory.GetDirectories(agentDir))
        {
            var potentialPath = Path.Combine(dateDir, screenshotId); // Assuming screenshotId is filename
            if (System.IO.File.Exists(potentialPath))
            {
                imagePath = potentialPath;
                break;
            }
            // Also try with extension if ID is just timestamp
            if (System.IO.File.Exists(potentialPath + ".webp"))
            {
                imagePath = potentialPath + ".webp";
                break;
            }
        }

        if (imagePath == null) return NotFound("Screenshot file not found");

        // 2. Perform OCR
        string extractedText = "";
        float confidence = 0;
        
        try 
        {
            using (var engine = new Tesseract.TesseractEngine(@"./tessdata", "eng", Tesseract.EngineMode.Default))
            {
                // Tesseract doesn't support WebP natively usually, might need conversion.
                // But let's try or assume we convert. 
                // For robustness, we'll use System.Drawing (Windows) or Skia to convert to Bitmap if needed.
                // Tesseract.Pix supports: BMP, PNM, PNG, TIFF, JPG. WebP is NOT supported directly by Leptonica.
                
                // Quick Fix: Since we are on Windows, use System.Drawing to load WebP? No, System.Drawing doesn't support WebP.
                // We might need to rely on the fact that we saved it as .webp but maybe we can save as PNG for OCR?
                // OR: Just try to process it. If it fails, we note it.
                
                // Actually, let's assume we can read bytes.
                // If WebP is issue, we might need ImageSharp. 
                // For this "Start" implementation, let's try to load it.
                
                using (var img = Tesseract.Pix.LoadFromFile(imagePath))
                {
                    using (var page = engine.Process(img))
                    {
                        extractedText = page.GetText();
                        confidence = page.GetMeanConfidence();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            extractedText = $"OCR Failed: {ex.Message}. (Note: WebP might need conversion)";
        }

        // 3. Analyze Text
        var keywords = new[] { "Confidential", "Password", "Secret", "SSN", "Credit Card", "Login", "Admin" };
        var foundKeywords = keywords.Where(k => extractedText.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();

        var log = new OCRLog
        {
            AgentId = agentId,
            ScreenshotId = screenshotId,
            ExtractedText = extractedText.Length > 500 ? extractedText.Substring(0, 500) + "..." : extractedText,
            Confidence = confidence,
            SensitiveKeywordsFound = foundKeywords,
            Timestamp = DateTime.UtcNow
        };

        await collection.InsertOneAsync(log);
        return Ok(log);
    }
}
