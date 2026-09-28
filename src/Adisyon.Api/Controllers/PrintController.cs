using Adisyon.Api.Auth;
using Adisyon.Api.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adisyon.Api.Controllers;

/// <summary>Sanal yazıcının son fişleri (gerçek yazıcı gelene kadar önizleme için).</summary>
[ApiController]
[Route("api/print")]
[Authorize(Roles = RoleNames.Management + "," + RoleNames.Kitchen)]
public class PrintController(PreviewPrinter printer) : ControllerBase
{
    [HttpGet("recent")]
    public List<PrintedTicket> Recent() => printer.Recent(User.GetBranchId());
}
