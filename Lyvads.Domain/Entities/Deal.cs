﻿
using Lyvads.Domain.Enums;

namespace Lyvads.Domain.Entities;

public class Deal : Entity
{
    public string? RequestId { get; set; }
    public Request Request { get; set; } = default!;
    public decimal Amount { get; set; }
    public DealStatus Status { get; set; } = DealStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Add these properties to fix the missing properties errors
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public string? CreatorId { get; set; }
    public Creator Creator { get; set; } = default!;
}
