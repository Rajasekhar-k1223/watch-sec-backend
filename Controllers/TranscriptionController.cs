using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Speech.Recognition;
using MongoDB.Driver;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/transcription")]
public class TranscriptionController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public TranscriptionController(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;
        _config = config;
    }

    [HttpPost("transcribe")]
    public async Task<IActionResult> TranscribeAudio([FromQuery] string agentId, [FromQuery] string filename)
    {
        // 1. Find Audio File
        var basePath = _config["StoragePath"] ?? "Storage";
        if (!Path.IsPathRooted(basePath)) basePath = Path.Combine(_env.ContentRootPath, basePath);
        
        var agentDir = Path.Combine(basePath, "Audio", agentId);
        if (!Directory.Exists(agentDir)) return NotFound("Agent audio storage not found");

        string? audioPath = null;
        // Search recursively
        foreach (var file in Directory.GetFiles(agentDir, filename, SearchOption.AllDirectories))
        {
            audioPath = file;
            break;
        }

        if (audioPath == null) return NotFound("Audio file not found");

        // 2. Transcribe (System.Speech)
        // Note: System.Speech requires the server to have Speech Recognition installed (Desktop Experience).
        // It runs on the server side.
        string text = "";
        
        await Task.Run(() => 
        {
            try 
            {
                using (var engine = new SpeechRecognitionEngine())
                {
                    engine.SetInputToWaveFile(audioPath);
                    engine.LoadGrammar(new DictationGrammar());
                    
                    // Recognize
                    var result = engine.Recognize();
                    text = result?.Text ?? "(No speech detected or low confidence)";
                }
            }
            catch (Exception ex)
            {
                text = $"Transcription Failed: {ex.Message}. (Ensure Server has Speech Runtime)";
            }
        });

        return Ok(new { Filename = filename, Transcription = text });
    }
}
