﻿using Lyvads.Application.Dtos;
using Lyvads.Application.Dtos.RegularUserDtos;
using Lyvads.Application.Interfaces;
using Lyvads.Domain.Entities;
using Lyvads.Domain.Repositories;
using Lyvads.Domain.Responses;
using Lyvads.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Text;

namespace Lyvads.API.Presentation.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class PaymentController : Controller
{
    private readonly IWalletService _walletService;
    private readonly IPaymentGatewayService _paymentService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PaymentController> _logger;
    private readonly string _paystackSecretKey;
    private readonly IConfiguration _configuration;
    private readonly IWalletRepository _walletRepository;
    private readonly IRequestRepository _requestRepository;

    public PaymentController(
        IConfiguration configuration,
        IWalletService walletService,
        IWalletRepository walletRepository,
        IRequestRepository requestRepository,
        UserManager<ApplicationUser> userManager,
        IPaymentGatewayService paymentService,
        ILogger<PaymentController> logger)
    {
        _configuration = configuration;
        _walletService = walletService;
        _userManager = userManager;
        _paymentService = paymentService;
        _logger = logger;
        _paystackSecretKey = _configuration["Paystack:PaystackSK"];
        _walletRepository = walletRepository;
        _requestRepository = requestRepository;
    }

    [HttpPost("fund-wallet")]
    public async Task<IActionResult> FundWallet(int amount)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return NotFound("User not found.");

        if (amount <= 0 || string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrWhiteSpace(user.FullName))
        {
            return BadRequest(new ServerResponse<string>
            {
                IsSuccessful = false,
                ResponseCode = "400",
                ResponseMessage = "Invalid input."
            });
        }

        var response = await _walletService.FundWalletAsync(amount, user.Email, user.FullName);
        if (!response.IsSuccessful)
            return BadRequest(response);

        return Ok(response);
    }


    [HttpGet("verify")]
    public async Task<IActionResult> VerifyPayment(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return BadRequest(new ServerResponse<string>
            {
                IsSuccessful = false,
                ResponseCode = "400",
                ResponseMessage = "Reference is required."
            });
        }

        var response = await _paymentService.VerifyPaymentAsync(reference);
        if (!response.IsSuccessful)
            return BadRequest(response);

        return Ok(response);
    }


    //[HttpGet("wallet-transactions")]
    //public async Task<IActionResult> GetWalletTransactions()
    //{
    //    var user = await _userManager.GetUserAsync(User);
    //    if (user == null)
    //    {
    //        return BadRequest(new ServerResponse<string>
    //        {
    //            IsSuccessful = false,
    //            ResponseCode = "400",
    //            ResponseMessage = "User not logged In."
    //        });
    //    }

    //    var response = await _walletService.GetWalletTransactions();
    //    if (!response.IsSuccessful)
    //        return BadRequest(response);

    //    return Ok(response);

    //}

    [HttpPost("paystack/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PaystackWebhook()
    {
        HttpContext.Request.EnableBuffering();
        string rawBody;

        using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        _logger.LogInformation("Webhook received with raw body: {RawBody}", rawBody);
        HttpContext.Request.Body.Position = 0;

        PaystackWebhookPayload payload;
        try
        {
            payload = JsonConvert.DeserializeObject<PaystackWebhookPayload>(rawBody);
            if (payload?.Data == null)
            {
                _logger.LogError("Invalid webhook payload format.");
                return BadRequest(new { status = "invalid_payload" });
            }

            _logger.LogInformation("Deserialized payload: {Payload}", JsonConvert.SerializeObject(payload));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize webhook payload.");
            return BadRequest(new { status = "invalid_payload_format" });
        }

        string trxRef = payload.Data.Reference;
        string status = payload.Data.Status;
        string email = payload.Data.Email;
        string authorizationCode = payload.Data.AuthorizationCode;

        if (string.IsNullOrEmpty(trxRef) || string.IsNullOrEmpty(status))
        {
            _logger.LogWarning("Transaction reference or status is missing.");
            return BadRequest(new { status = "missing_data" });
        }

        var transaction = await _walletRepository.GetTransactionByTrxRefAsync(trxRef);
        if (transaction == null)
        {
            _logger.LogWarning("Transaction with reference {TrxRef} not found.", trxRef);
            return BadRequest(new { status = "transaction_not_found" });
        }

        if (transaction.Status)
        {
            _logger.LogInformation("Transaction with reference {TrxRef} already processed.", trxRef);
            return Ok(new { status = "already_processed" });
        }

        if (status == "success")
        {
            transaction.Status = true;

            // Check if the transaction is for a request to a creator
            if (transaction.RequestId != null)
            {
                var request = await _requestRepository.GetRequestByIdAsync(transaction.RequestId);
                if (request != null && request.CreatorId != null)
                {
                    decimal baseAmount = request.RequestAmount;
                    decimal fastTrackFee = request.FastTrackFee;

                    // Credit the creator's wallet with the base amount and fast track fee
                    await _walletService.CreditWalletAmountAsync(request.CreatorId, baseAmount + fastTrackFee);
                    _logger.LogInformation("Credited {Amount} to Creator ID: {CreatorId}", baseAmount + fastTrackFee, request.CreatorId);
                }
            }

            if (transaction.WalletId != null)
            {
                var wallet = await _walletRepository.GetWalletByIdAsync(transaction.WalletId);
                if (wallet != null)
                {
                    wallet.Balance += transaction.Amount;
                    await _walletRepository.UpdateWalletAsync(wallet);
                    _logger.LogInformation("Wallet balance updated for WalletId: {WalletId}.", transaction.WalletId);
                }
            }

            // Store the card details after a successful payment
            var storeCardRequest = new StoreCardRequest
            {
                AuthorizationCode = authorizationCode,
                Email = email,
                CardType = payload.Data.CardType,
                Last4 = payload.Data.Last4,
                ExpMonth = payload.Data.ExpiryMonth,
                ExpYear = payload.Data.ExpiryYear,
                Bank = payload.Data.Bank,
                AccountName = payload.Data.AccountName,
                Reusable = payload.Data.Reusable,
                CountryCode = payload.Data.CountryCode,
                Bin = payload.Data.Bin,
                Signature = payload.Data.Signature,
                Channel = payload.Data.Channel
            };

            await StoreCardForRecurringPayment(storeCardRequest);

            await _walletRepository.UpdateTransactionAsync(transaction);
            _logger.LogInformation("Transaction with reference {TrxRef} marked as successful.", trxRef);
            return Ok(new { status = "success" });
        }

        transaction.Status = false;
        await _walletRepository.UpdateTransactionAsync(transaction);
        _logger.LogInformation("Transaction with reference {TrxRef} marked as failed.", trxRef);
        return Ok(new { status = "failure" });
    }


    //[HttpPost("paystack/webhook")]
    //[AllowAnonymous]
    //public async Task<IActionResult> PaystackWebhook()
    //{
    //    HttpContext.Request.EnableBuffering();
    //    string rawBody;

    //    using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
    //    {
    //        rawBody = await reader.ReadToEndAsync();
    //    }

    //    _logger.LogInformation("Webhook received with raw body: {RawBody}", rawBody);
    //    HttpContext.Request.Body.Position = 0;

    //    PaystackWebhookPayload payload;
    //    try
    //    {
    //        payload = JsonConvert.DeserializeObject<PaystackWebhookPayload>(rawBody);
    //        if (payload?.Data == null)
    //        {
    //            _logger.LogError("Invalid webhook payload format.");
    //            return BadRequest(new { status = "invalid_payload" });
    //        }

    //        _logger.LogInformation("Deserialized payload: {Payload}", JsonConvert.SerializeObject(payload));
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Failed to deserialize webhook payload.");
    //        return BadRequest(new { status = "invalid_payload_format" });
    //    }

    //    string trxRef = payload.Data.Reference;
    //    string status = payload.Data.Status;

    //    if (string.IsNullOrEmpty(trxRef) || string.IsNullOrEmpty(status))
    //    {
    //        _logger.LogWarning("Transaction reference or status is missing.");
    //        return BadRequest(new { status = "missing_data" });
    //    }

    //    var transaction = await _walletRepository.GetTransactionByTrxRefAsync(trxRef);
    //    if (transaction == null)
    //    {
    //        _logger.LogWarning("Transaction with reference {TrxRef} not found.", trxRef);
    //        return BadRequest(new { status = "transaction_not_found" });
    //    }

    //    if (transaction.Status)
    //    {
    //        _logger.LogInformation("Transaction with reference {TrxRef} already processed.", trxRef);
    //        return Ok(new { status = "already_processed" });
    //    }

    //    if (status == "success")
    //    {
    //        transaction.Status = true;
    //        if (transaction.WalletId != null)
    //        {
    //            var wallet = await _walletRepository.GetWalletByIdAsync(transaction.WalletId);
    //            if (wallet != null)
    //            {
    //                wallet.Balance += transaction.Amount;
    //                await _walletRepository.UpdateWalletAsync(wallet);
    //                _logger.LogInformation("Wallet balance updated for WalletId: {WalletId}.", transaction.WalletId);
    //            }
    //        }

    //        await _walletRepository.UpdateTransactionAsync(transaction);
    //        _logger.LogInformation("Transaction with reference {TrxRef} marked as successful.", trxRef);
    //        return Ok(new { status = "success" });
    //    }

    //    transaction.Status = false;
    //    await _walletRepository.UpdateTransactionAsync(transaction);
    //    _logger.LogInformation("Transaction with reference {TrxRef} marked as failed.", trxRef);
    //    return Ok(new { status = "failure" });
    //}

    
    [HttpPost("paystack/store-card")]
    public async Task<IActionResult> StoreCardForRecurringPayment([FromBody] StoreCardRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.AuthorizationCode) || string.IsNullOrEmpty(request.Email))
        {
            _logger.LogError("Invalid request data.");
            return BadRequest(new { status = "invalid_data", message = "Authorization code and email are required." });
        }

        try
        {
            // Check if the email already has a stored card
            var existingCard = await _walletRepository.GetCardAuthorizationByEmailAsync(request.Email);
            if (existingCard != null)
            {
                _logger.LogInformation("Card already stored for email: {Email}", request.Email);
                return Ok(new { status = "card_already_stored", message = "Card is already stored for this email." });
            }

            // Store the card authorization details
            var cardAuthorization = new CardAuthorization
            {
                AuthorizationCode = request.AuthorizationCode,
                Email = request.Email,
                CardType = request.CardType,
                Last4 = request.Last4,
                ExpiryMonth = request.ExpMonth,
                ExpiryYear = request.ExpYear,
                Bank = request.Bank,
                AccountName = request.AccountName,
                Reusable = request.Reusable,
                CountryCode = request.CountryCode
            };

            await _walletRepository.StoreCardAuthorizationAsync(cardAuthorization);

            _logger.LogInformation("Card stored successfully for email: {Email}", request.Email);

            return Ok(new { status = "success", message = "Card stored successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing card authorization.");
            return StatusCode(500, new { status = "error", message = "An error occurred while storing the card." });
        }
    }




    //[HttpPost("paystack/webhook")]
    //[AllowAnonymous]
    //public async Task<IActionResult> PaystackWebhook()
    //{
    //    // Enable buffering to allow reading the request body multiple times
    //    HttpContext.Request.EnableBuffering();

    //    // Read raw body from the request
    //    string rawBody;
    //    using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
    //    {
    //        rawBody = await reader.ReadToEndAsync();
    //    }

    //    // Log the raw body for debugging purposes
    //    _logger.LogInformation("Raw Webhook Body: {Body}", rawBody);

    //    // Reset the stream position to allow further use of the request body
    //    HttpContext.Request.Body.Position = 0;

    //    // Deserialize the payload
    //    PaystackWebhookPayload payload;
    //    try
    //    {
    //        payload = JsonConvert.DeserializeObject<PaystackWebhookPayload>(rawBody);
    //        if (payload == null || payload.Data == null)
    //        {
    //            _logger.LogError("Webhook payload or Data is null.");
    //            return BadRequest(new ServerResponse<string>
    //            {
    //                IsSuccessful = false,
    //                ResponseCode = "400",
    //                ResponseMessage = "Invalid payload format."
    //            });
    //        }

    //        _logger.LogInformation("Deserialized Paystack webhook payload: {Payload}", JsonConvert.SerializeObject(payload));
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Failed to deserialize webhook payload.");
    //        return BadRequest(new ServerResponse<string>
    //        {
    //            IsSuccessful = false,
    //            ResponseCode = "400",
    //            ResponseMessage = "Invalid payload format."
    //        });
    //    }

    //    // Validate status field
    //    if (string.IsNullOrEmpty(payload.Data.Status))
    //    {
    //        _logger.LogWarning("Status field is null or empty.");
    //        return BadRequest(new ServerResponse<string>
    //        {
    //            IsSuccessful = false,
    //            ResponseCode = "400",
    //            ResponseMessage = "Invalid payload: Status is required."
    //        });
    //    }

    //    // Store or update transaction
    //    await _paymentService.StoreTransactionAsync(payload);

    //    var trxRef = payload?.Data?.Reference;
    //    var status = payload?.Data?.Status; // status can be 'success' or 'failed'

    //    // Get the transaction from the database
    //    var transaction = await _walletRepository.GetTransactionByTrxRefAsync(trxRef);
    //    if (transaction == null)
    //    {
    //        return BadRequest(new ServerResponse<string>
    //        {
    //            IsSuccessful = false,
    //            ResponseCode = "404",
    //            ResponseMessage = "Transaction not found."
    //        });
    //    }

    //    // If the status is success, update the transaction and wallet
    //    // If the status is success, update the transaction and make request status
    //    if (status == "success")
    //    {
    //        // Update the transaction status
    //        transaction.Status = true;

    //        // If there is no walletId, it means this transaction is related to a request and not wallet funding
    //        if (transaction.WalletId == null)
    //        {
    //            // Update the associated request (if applicable) to mark it as completed
    //            var request = await _requestRepository.GetRequestByTransactionRefAsync(trxRef);
    //            if (request != null)
    //            {
    //                request.TransactionStatus = true; // Mark the request as completed
    //                await _requestRepository.UpdateRequestAsync(request);
    //            }
    //        }
    //        else
    //        {
    //            // Now update the wallet balance
    //            var wallet = await _walletRepository.GetWalletByIdAsync(transaction.WalletId);
    //            if (wallet != null)
    //            {
    //                wallet.Balance += transaction.Amount;
    //                await _walletRepository.UpdateWalletAsync(wallet);
    //            }
    //        }

    //        // Finally, update the transaction in the database
    //        await _walletRepository.UpdateTransactionAsync(transaction);

    //        return Ok(new { status = "success" });
    //    }

    //    // If the payment failed, you can mark the transaction as failed or handle accordingly
    //    transaction.Status = false;
    //    await _walletRepository.UpdateTransactionAsync(transaction);

    //    return Ok(new { status = "failure" });


    //}


    //[HttpPost("paystack/webhook")]
    //public async Task<IActionResult> PaystackWebhook([FromBody] PaystackWebhookPayload payload, [FromHeader(Name = "x-paystack-signature")] string signature)
    //{
    //    // Log that the webhook endpoint was hit
    //    Console.WriteLine("Paystack webhook triggered.");
    //    _logger.LogInformation("Paystack webhook triggered.");

    //    // Verify Paystack webhook signature
    //    var isValid = _paymentService.VerifyPaystackSignature(payload, signature, _paystackSecretKey);
    //    if (!isValid)
    //    {
    //        Console.WriteLine("Invalid Paystack webhook signature.");
    //        _logger.LogWarning("Invalid Paystack webhook signature.");
    //        return BadRequest(new ServerResponse<string>
    //        {
    //            IsSuccessful = false,
    //            ResponseCode = "400",
    //            ResponseMessage = "Invalid signature."
    //        });
    //    }

    //    // Log the received payload for debugging purposes
    //    Console.WriteLine("Webhook Payload: " + JsonConvert.SerializeObject(payload));
    //    _logger.LogInformation("Webhook Payload: {Payload}", JsonConvert.SerializeObject(payload));

    //    // Find the transaction in your database
    //    var transaction = await _paymentService.GetTransactionByReferenceAsync(payload.Data.Reference);
    //    if (transaction == null)
    //    {
    //        Console.WriteLine("Transaction not found for reference: " + payload.Data.Reference);
    //        _logger.LogWarning("Transaction not found for reference: {Reference}", payload.Data.Reference);
    //        return NotFound(new ServerResponse<string>
    //        {
    //            IsSuccessful = false,
    //            ResponseCode = "404",
    //            ResponseMessage = "Transaction not found."
    //        });
    //    }

    //    // Log before updating transaction status
    //    Console.WriteLine($"Updating transaction status for reference: {transaction.TrxRef}");
    //    _logger.LogInformation("Updating transaction status for reference: {Reference}", transaction.TrxRef);

    //    // Update the transaction status
    //    transaction.Status = payload.Data.Status == "success";

    //    // Save the updated transaction
    //    try
    //    {
    //        await _paymentService.UpdateTransactionAsync(transaction);
    //        Console.WriteLine($"Transaction status updated to {transaction.Status} for reference: {transaction.TrxRef}");
    //        _logger.LogInformation("Transaction status updated to {Status} for reference: {Reference}", transaction.Status, transaction.TrxRef);
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine("Error updating transaction: " + ex.Message);
    //        _logger.LogError(ex, "Error updating transaction for reference: {Reference}", transaction.TrxRef);
    //        return StatusCode(500, new ServerResponse<string>
    //        {
    //            IsSuccessful = false,
    //            ResponseCode = "500",
    //            ResponseMessage = "Internal Server Error. Unable to update transaction status."
    //        });
    //    }

    //    // Return success response to Paystack
    //    Console.WriteLine("Webhook processed successfully.");
    //    _logger.LogInformation("Webhook processed successfully.");
    //    return Ok(new { status = "success" });
    //}

}

