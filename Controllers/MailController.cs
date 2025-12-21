using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace watch_sec_backend.Controllers;

[ApiController]
[Route("api/mail")]
[Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
public class MailController : ControllerBase
{
    private readonly IMongoClient _mongo;

    public MailController(IMongoClient mongo)
    {
        _mongo = mongo;
    }

    [HttpGet]
    public async Task<IActionResult> GetMailLogs()
    {
        var db = _mongo.GetDatabase("watchsec");
        var collection = db.GetCollection<MailLog>("mail_logs");
        
        var sort = Builders<MailLog>.Sort.Descending(x => x.Timestamp);
        var logs = await collection.Find(Builders<MailLog>.Filter.Empty).Sort(sort).Limit(100).ToListAsync();
        
        return Ok(logs);
    }
}
