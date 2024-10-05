﻿using Lyvads.API.Presentation.Dtos;
using Lyvads.API.Extensions;
using Lyvads.Application.Interfaces;
using Lyvads.Application.Dtos.AuthDtos;
using Microsoft.AspNetCore.Mvc;
using static Lyvads.Application.Implementations.AuthService;
using System.ComponentModel.DataAnnotations;
using Lyvads.Application.Dtos;
using Lyvads.Application.Dtos.CreatorDtos;
using Lyvads.Application.Implementations;
using Microsoft.AspNetCore.Identity;
using Lyvads.Domain.Responses;

namespace Lyvads.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IVerificationService _verificationService;
    private readonly ILogger<AuthController> _logger;
    private readonly IEmailContext _emailContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthController(IVerificationService verificationService, 
        IAuthService authService, 
        ILogger<AuthController> logger, 
        IEmailContext emailContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _verificationService = verificationService;
        _authService = authService;
        _logger = logger;
        _emailContext = emailContext;
        _emailContext = emailContext;
        _httpContextAccessor = httpContextAccessor; 
    }

    [HttpPost("Initiate")]
    public async Task<IActionResult> InitiateRegistration([FromBody] InitiateRegistrationDto dto)
    {
        _logger.LogInformation("******* Initiate Registration ********");

        var result = await _authService.InitiateRegistration(dto.Email);

        // Log the server response, useful for debugging
        _logger.LogInformation($"Initiate registration result: {System.Text.Json.JsonSerializer.Serialize(result)}");

        if (!result.IsSuccessful)
        {
            _logger.LogWarning($"Failed to initiate registration for email: {dto.Email}.");
            return BadRequest(result.ErrorResponse);
        }

        return Ok(ResponseDto<RegistrationResponseDto>.Success(result.Data, "Verification code sent. Please check your email."));
    }


    [HttpPost("VerifyEmail")]
    public async Task<IActionResult> VerifyEmail([FromBody] EmailVerificationDto dto)
    {
        _logger.LogInformation("******* Inside the VerifyEmail Controller Method ********");

        var result = await _authService.VerifyEmail(dto.VerificationCode);

        if (!result.IsSuccessful)
            return BadRequest(result.ErrorResponse);

        return Ok(ResponseDto<EmailVerificationResponseDto>.Success(result.Data, "Email verification successful."));
    }


    [HttpPost("RegisterUser")]
    public async Task<IActionResult> RegisterUser([FromBody] CompleteRegistrationDto dto)
    {
        _logger.LogInformation($"******* Inside the RegisterUser Controller Method ********");

        // Fetch the verified email directly from the verification service
        //var verifiedEmail = _emailContext.VerifiedEmail;
        var verifiedEmail = _httpContextAccessor.HttpContext?.Session.GetString("VerifiedEmail");


        if (string.IsNullOrEmpty(verifiedEmail))
        {
            _logger.LogWarning("No verified email found for the provided email.");
            return BadRequest(ResponseDto<object>.Failure(new[] { new Error("Email.Error", "Email verification is required") }));
        }

        var registerUserDto = new RegisterUserDto
        {
            FullName = dto.FullName,
            AppUserName = dto.AppUserName,
            PhoneNumber = dto.PhoneNumber,
            Password = dto.Password,
            ConfirmPassword = dto.ConfirmPassword,
            Email = verifiedEmail
        };

        var result = await _authService.RegisterUser(registerUserDto);

        if (!result.IsSuccessful)
            return BadRequest(result.ErrorResponse);

        return Ok(ResponseDto<RegisterUserResponseDto>.Success(result.Data, "User registered successfully."));
    }


    [HttpPost("RegisterCreator")]
    public async Task<IActionResult> RegisterCreator([FromBody] CompleteRegistrationDto dto)
    {
        _logger.LogInformation($"******* Inside the RegisterCreator Controller Method ********");

        // Fetch the verified email directly from the verification service
        //var email = await _verificationService.GetVerifiedEmail(_emailContext.VerifiedEmail);
        var email = _httpContextAccessor.HttpContext?.Session.GetString("VerifiedEmail");


        if (string.IsNullOrEmpty(email))
            return BadRequest(ResponseDto<object>.Failure(new[] { new Error("Email.Error", "Email verification is required") }));

        var registerCreatorDto = new RegisterCreatorDto
        {
            FullName = dto.FullName,
            AppUserName = dto.AppUserName,
            PhoneNumber = dto.PhoneNumber,
            Password = dto.Password,
            ConfirmPassword = dto.ConfirmPassword,
            Email = email
        };

        var result = await _authService.RegisterCreator(registerCreatorDto);

        if (!result.IsSuccessful)
            return BadRequest(result.ErrorResponse);

        return Ok(ResponseDto<RegisterUserResponseDto>.Success(result.Data, "Creator registered successfully."));
    }


    [HttpPost("RegisterSuperAdmin")]
    public async Task<IActionResult> RegisterSuperAdmin([FromBody] CompleteRegistrationDto dto)
    {
        _logger.LogInformation($"******* Inside the RegisterSuperAdmin Controller Method ********");

        // Fetch the verified email directly from the verification service
        //var email = await _verificationService.GetVerifiedEmail(_emailContext.VerifiedEmail);
        var email = _httpContextAccessor.HttpContext?.Session.GetString("VerifiedEmail");


        if (string.IsNullOrEmpty(email))
            return BadRequest(ResponseDto<object>.Failure(new[] { new Error("Email.Error", "Email verification is required") }));

        var registerSuperAdminDto = new RegisterSuperAdminDto
        {
            FullName = dto.FullName,
            AppUserName = dto.AppUserName,
            PhoneNumber = dto.PhoneNumber,
            Password = dto.Password,
            ConfirmPassword = dto.ConfirmPassword,
            Email = email
        };

        var result = await _authService.RegisterSuperAdmin(registerSuperAdminDto);

        if (!result.IsSuccessful)
            return BadRequest(result.ErrorResponse);

        return Ok(ResponseDto<RegisterUserResponseDto>.Success(result.Data, "Super admin registered successfully."));
    }


    [HttpPost("Login")]
    public async Task<IActionResult> Login([FromBody] LoginUserDto loginUserDto)
    {
        _logger.LogInformation($"******* Inside the Login Controller Method ********");

        var result = await _authService.Login(loginUserDto);

        if (!result.IsSuccessful)
            return BadRequest(result.ErrorResponse);

        return Ok(ResponseDto<object>.Success(new
        {
            Token = result.Data.Token,
            FullName = result.Data.FullName,
            Roles = result.Data.Roles,
            Email = result.Data.Email
        }, "Login successful."));
    }


    [HttpPost("ForgotPassword")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto forgotPasswordDto)
    {
        _logger.LogInformation("******* Inside the ForgotPassword Controller Method ********");

        var result = await _authService.ForgotPassword(forgotPasswordDto);

        if (!result.IsSuccessful)
            return BadRequest(result.ErrorResponse);

        return Ok(ResponseDto<object>.Success(result.Data, "Password reset email sent successfully."));
    }


    [HttpPost("VerifyCodeAndResetPassword")]
    public async Task<IActionResult> VerifyCodeAndResetPassword([FromBody] ResetPasswordWithCodeDto resetPasswordDto)
    {
        _logger.LogInformation("******* Inside the VerifyCodeAndResetPassword Controller Method ********");

        var result = await _authService.VerifyCodeAndResetPassword(resetPasswordDto);

        if (!result.IsSuccessful)
            return BadRequest(result.ErrorResponse);

        var passwordResetResponse = new PasswordResetResponseDto
        {
            Email = result.Data.Email,
            NewPassword = result.Data.NewPassword,
            Message = "Password reset successful"
        };

        return Ok(ResponseDto<PasswordResetResponseDto>.Success(passwordResetResponse, "Password reset successful."));
    }


    // POST: api/admin/forgot-password
    [HttpPost("Admin/ForgotPassword")]
    public async Task<ActionResult<ServerResponse<RegistrationResponseDto>>> AdminForgotPassword([FromBody] ForgotPasswordRequestDto forgotPasswordDto)
    {
        if (forgotPasswordDto == null)
        {
            return BadRequest(new ServerResponse<RegistrationResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Auth.Error",
                    ResponseDescription = "Request body is null"
                }
            });
        }

        var response = await _authService.AdminForgotPassword(forgotPasswordDto);
        return Ok(response);
    }

    // POST: api/admin/verify-code
    [HttpPost("Admin/VerifyCode")]
    public async Task<ActionResult<ServerResponse<string>>> VerifyAdminVerificationCode([FromBody] string verificationCode)
    {
        if (string.IsNullOrWhiteSpace(verificationCode))
        {
            return BadRequest(new ServerResponse<string>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Verification.Error",
                    ResponseDescription = "Verification code is required"
                }
            });
        }

        var response = await _authService.VerifyAdminVerificationCode(verificationCode);
        return Ok(response);
    }

    // POST: api/admin/reset-password
    [HttpPost("Admin/ResetPassword")]
    public async Task<ActionResult<ServerResponse<PasswordResetResponseDto>>> ResetAdminPassword([FromBody] ResetPasswordWithCodeDto resetPasswordDto)
    {
        if (resetPasswordDto == null)
        {
            return BadRequest(new ServerResponse<PasswordResetResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Auth.Error",
                    ResponseDescription = "Request body is null"
                }
            });
        }

        // Retrieve the email from the session or another mechanism, if applicable
        var email = HttpContext.Session.GetString("VerifiedAdminEmail");
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new ServerResponse<PasswordResetResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Auth.Error",
                    ResponseDescription = "Email not found in session"
                }
            });
        }

        var response = await _authService.ResetAdminPassword(resetPasswordDto, email);
        return Ok(response);
    }


















    //[HttpGet("ConfirmEmail")]
    //public async Task<IActionResult> ConfirmEmail([FromQuery] string email, [FromQuery] string token)
    //{
    //    _logger.LogInformation($"******* Inside the ConfirmEmail Controller Method ********");

    //    var result = await _authService.ConfirmEmail(email, token);

    //    if (result.IsFailure)
    //        return BadRequest(ResponseDto<object>.Failure(result.Errors));

    //    return Ok(ResponseDto<object>.Success(result.Message));
    //}

    //[HttpPost("ChangePassword")]
    //public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto changePasswordDto)
    //{
    //    _logger.LogInformation($"******* Inside the Change Password Controller Method ********");

    //    if (!ModelState.IsValid)
    //    {
    //        return BadRequest(ResponseDto<object>.Failure(ModelState.GetErrors()));
    //    }
    //    var changePasswordResult = await _authService.ChangePasswordAsync(changePasswordDto);
    //    if (changePasswordResult.IsFailure)
    //        return BadRequest(ResponseDto<object>.Failure(changePasswordResult.Errors));

    //    return Ok(ResponseDto<object>.Success(changePasswordResult.Message));
    //}

    //[HttpPost("ResetPassword")]
    //public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
    //{
    //    _logger.LogInformation($"******* Inside the Reset Password Controller Method ********");

    //    if (!ModelState.IsValid)
    //    {
    //        return BadRequest(ResponseDto<object>.Failure(ModelState.GetErrors()));
    //    }

    //    var resetPasswordResult = await _authService.ResetPasswordAsync(resetPasswordDto);

    //    if (resetPasswordResult.IsFailure)
    //        return BadRequest(ResponseDto<object>.Failure(resetPasswordResult.Errors));

    //    return Ok(ResponseDto<object>.Success(resetPasswordResult));
    //}




    public static class EmailContext
    {
        public static string VerifiedEmail { get; set; } = string.Empty;
    }

    public class EmailHolder
    {
        public string? Email { get; set; }
    }
    public class EmailVerificationDto
    {
        public required string VerificationCode { get; set; }
    }

    public class InitiateRegistrationDto
    {
        public required string Email { get; set; }
    }

    public class CompleteRegistrationDto
    {
        [Required] public required string FullName { get; init; }
        [Required] public required string AppUserName { get; init; }
        [Required] public required string PhoneNumber { get; init; }

        [Required(ErrorMessage = "New password is required")]
        [DataType(DataType.Password)]
        public required string Password { get; set; }

        [Required(ErrorMessage = "Confirm password is required")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Password and confirm password do not match")]
        public required string ConfirmPassword { get; set; }
    }
}