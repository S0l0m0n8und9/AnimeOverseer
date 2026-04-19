using Microsoft.AspNetCore.Mvc;
using AnimeOverseer.Server.Models;

namespace AnimeOverseer.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RequestController : ControllerBase
{
    private readonly List<Request> _requests = new();

    [HttpGet]
    public IActionResult GetRequests()
    {
        return Ok(_requests);
    }

    [HttpPost]
    public IActionResult CreateRequest([FromBody] CreateRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest("Title is required.");

        var request = new Request
        {
            Title = dto.Title,
            Description = dto.Description,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
            RequestedBy = dto.RequestedBy
        };

        _requests.Add(request);
        return CreatedAtAction(nameof(GetRequests), new { id = request.Id }, request);
    }

    [HttpPut("{id}/status")]
    public IActionResult UpdateStatus(int id, [FromBody] string status)
    {
        var request = _requests.FirstOrDefault(r => r.Id == id);
        if (request == null)
            return NotFound();

        request.Status = status;
        request.UpdatedAt = DateTime.UtcNow;
        return Ok(request);
    }
}

public class CreateRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RequestedBy { get; set; }
}