﻿using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;

namespace Lyvads.Domain.Entities;

public class ApplicationUser : IdentityUser, IAuditable
{
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string PhoneNumber { get; set; }
    public string? Username { get; set; }
    public string? ImageUrl { get; set; }
    public string? Occupation { get; set; }
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public string? PublicId { get; set; } = default!;
    public decimal WalletBalance { get; set; } = default!;
    public bool IsVerified { get; set; }
    public string? FullName => $"{FirstName} {LastName}";
    public string? VerificationCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string WalletId { get; set; } = default!;
    public Wallet Wallet { get; set; } = default!;
    public ICollection<Notification>? Notifications { get; set; } = new List<Notification>();
    public string? StripeAccountId { get; set; } = default!;
}
