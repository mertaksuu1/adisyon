using System.ComponentModel.DataAnnotations;
using Adisyon.Api.Auth;
using Adisyon.Api.Data;
using Adisyon.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adisyon.Api.Controllers;

/// <summary>Masalar. Her kullanıcı giriş yaptığı cihazın şubesindeki masaları görür.</summary>
[ApiController]
[Route("api/tables")]
public class TablesController(AdisyonDbContext db) : ControllerBase
{
    /// <summary>
    /// Şubedeki masalar ve her birinin durumu: açık adisyonu varsa "dolu" (tutar ve açılış saatiyle), yoksa "boş".
    /// Garson ve kasa ekranındaki masa planı bu listeden çizilecek.
    /// </summary>
    [HttpGet]
    public async Task<List<TableDto>> List(CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();

        // Tek sorguda masalar + açık adisyonların toplamı. EF bunu tek bir SQL'e çevirir;
        // her masa için ayrı sorgu atmaz.
        return await db.Tables
            .Where(t => t.BranchId == branchId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new TableDto(
                t.Id,
                t.Name,
                t.SortOrder,
                t.IsActive,
                t.QrToken,
                db.TableSessions
                    .Where(s => s.TableId == t.Id && s.Status == TableSessionStatus.Open)
                    .Select(s => new OpenSessionSummary(
                        s.Id,
                        s.OpenedAt,
                        s.Orders.Where(o => o.Status != OrderStatus.Cancelled)
                            .SelectMany(o => o.Items)
                            .Sum(i => i.UnitPrice * i.Quantity)))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Management)]
    public async Task<ActionResult<TableDto>> Create(SaveTableRequest request, CancellationToken cancellationToken)
    {
        var table = new DiningTable
        {
            BranchId = User.GetBranchId(),
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            QrToken = DevDataSeeder.NewQrToken(),
        };
        db.Tables.Add(table);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(List), TableDto.From(table));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Management)]
    public async Task<ActionResult<TableDto>> Update(Guid id, SaveTableRequest request, CancellationToken cancellationToken)
    {
        var branchId = User.GetBranchId();
        var table = await db.Tables.SingleOrDefaultAsync(t => t.Id == id && t.BranchId == branchId, cancellationToken);
        if (table is null)
        {
            return NotFound();
        }

        table.Name = request.Name.Trim();
        table.SortOrder = request.SortOrder;
        table.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return TableDto.From(table);
    }
}

public record SaveTableRequest(
    [Required, MaxLength(100)] string Name,
    int SortOrder = 0,
    bool IsActive = true);

/// <summary>OpenSession null ise masa boştur.</summary>
public record TableDto(Guid Id, string Name, int SortOrder, bool IsActive, string QrToken, OpenSessionSummary? OpenSession)
{
    public static TableDto From(DiningTable t) => new(t.Id, t.Name, t.SortOrder, t.IsActive, t.QrToken, null);
}

public record OpenSessionSummary(Guid SessionId, DateTimeOffset OpenedAt, decimal Total);
