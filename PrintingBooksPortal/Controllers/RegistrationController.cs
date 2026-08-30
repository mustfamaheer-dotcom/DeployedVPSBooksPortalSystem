using Microsoft.AspNetCore.Mvc;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/registration")]
public class RegistrationController : ControllerBase
{
    private readonly RegistrationService _registrationService;
    private readonly StudentService _studentService;

    public RegistrationController(RegistrationService registrationService, StudentService studentService)
    {
        _registrationService = registrationService;
        _studentService = studentService;
    }

    [HttpPost("teacher")]
    public async Task<IActionResult> RegisterTeacher([FromForm] string name, [FromForm] string email, [FromForm] string? phone, [FromForm] string organization, [FromForm] string? message, [FromForm] string password)
    {
        await _registrationService.SubmitTeacherRequestAsync(name, email, phone, organization, message, password);
        return Ok(new { success = true });
    }

    [HttpPost("bookshop")]
    public async Task<IActionResult> RegisterBookshop([FromForm] string name, [FromForm] string email, [FromForm] string phone, [FromForm] string bookshopName, [FromForm] string address, [FromForm] string? message, [FromForm] string password)
    {
        await _registrationService.SubmitBookshopRequestAsync(name, email, phone, bookshopName, address, message, password);
        return Ok(new { success = true });
    }

    [HttpPost("student")]
    public async Task<IActionResult> RegisterStudent([FromForm] string name, [FromForm] string email, [FromForm] string password, [FromForm] string? message, [FromForm] List<int> tenantIds)
    {
        if (tenantIds == null || tenantIds.Count == 0)
            return BadRequest(new { success = false, error = "Must select at least one teacher" });

        var user = await _studentService.RegisterStudentAsync(name, email, password, tenantIds, message);
        if (user == null)
            return BadRequest(new { success = false, error = "Failed to create user. Email may be taken." });

        return Ok(new { success = true });
    }
}
