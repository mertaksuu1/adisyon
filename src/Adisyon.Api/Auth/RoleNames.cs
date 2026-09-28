using Adisyon.Api.Domain;

namespace Adisyon.Api.Auth;

/// <summary>
/// [Authorize(Roles = ...)] içinde kullanılacak rol adları. Attribute'lar sabit (const) metin
/// istediği için enum'u doğrudan veremiyoruz; nameof ile enum'dan türetiyoruz ki yazım hatası olmasın.
/// </summary>
public static class RoleNames
{
    public const string Owner = nameof(UserRole.Owner);
    public const string Manager = nameof(UserRole.Manager);
    public const string Waiter = nameof(UserRole.Waiter);
    public const string Kitchen = nameof(UserRole.Kitchen);
    public const string Cashier = nameof(UserRole.Cashier);

    /// <summary>Virgülle ayrılmış liste "bunlardan herhangi biri" demektir.</summary>
    public const string Management = Owner + "," + Manager;
}
