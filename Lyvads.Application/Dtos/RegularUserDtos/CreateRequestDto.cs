﻿
using Lyvads.Domain.Enums;

namespace Lyvads.Application.Dtos.RegularUserDtos;

public class CreateRequestDto
{
    public string? creatorId {  get; set; }
    public string? Script { get; set; }
    public string? RequestType { get; set; }
    public int Amount { get; set; }
    public bool FastTrack { get; set; } 
    public bool RemoveWatermark { get; set; } 
    public bool CreatorPost { get; set; } 
    public AppPaymentMethod payment {  get; set; }
}

public class PaymentDTO
{
    public string? ProductName { get; set; }
    public int Amount { get; set; }
    public string? ReturnUrl { get; set; }
    public AppPaymentMethod? Method { get; set; } // Card, Wallet, Online
    public string? UserId { get; set; }
}