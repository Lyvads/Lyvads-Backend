﻿

using Lyvads.Application.Dtos.AuthDtos;
using Lyvads.Application.Dtos;
using Lyvads.Application.Interfaces;
using Lyvads.Domain.Constants;
using Lyvads.Domain.Entities;
using Lyvads.Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using Lyvads.Infrastructure.Repositories;
using Lyvads.Application.Dtos.SuperAdminDtos;
using Lyvads.Domain.Responses;

namespace Lyvads.Application.Implementations;

public class AdminUserService : ISuperAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAdminRepository _adminRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AdminDashboardService> _logger;
    private readonly IRegularUserRepository _regularUserRepository;
    private readonly ICreatorRepository _creatorRepository;
    private readonly ISuperAdminRepository _superAdminRepository;


    public AdminUserService(
        UserManager<ApplicationUser> userManager,
        IAdminRepository adminRepository,
        ICurrentUserService currentUserService,
        ILogger<AdminDashboardService> logger,
        ISuperAdminRepository superAdminRepository,
        ICreatorRepository creatorRepository,
        IRegularUserRepository regularUserRepository
        )
    {
        _userManager = userManager;
        _adminRepository = adminRepository;
        _currentUserService = currentUserService;
        _logger = logger;
        _regularUserRepository = regularUserRepository;
        _creatorRepository = creatorRepository;
        _superAdminRepository = superAdminRepository;
    }

    public async Task<ServerResponse<List<UserDto>>> GetUsers(string role = null!, bool sortByDate = true)
    {
        try
        {
            var users = _userManager.Users.AsQueryable();

            if (!string.IsNullOrEmpty(role))
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role);
                users = users.Where(u => usersInRole.Contains(u));
            }

            if (sortByDate)
            {
                users = users.OrderBy(u => u.CreatedAt);
            }

            var userDtos = new List<UserDto>();
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var roleName = roles.FirstOrDefault();

                userDtos.Add(new UserDto
                {
                    UserId = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Email = user.Email,
                    Role = roleName,
                    CreatedAt = user.CreatedAt
                });
            }

            _logger.LogInformation("Successfully retrieved user list");
            return new ServerResponse<List<UserDto>> { IsSuccessful = true, Data = userDtos };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching users");
            return new ServerResponse<List<UserDto>>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "500",
                    ResponseMessage = "Internal Server Error",
                    ResponseDescription = ex.Message
                }
            };
        }
    }

    public async Task<ServerResponse<AddUserResponseDto>> AddUser(RegisterUserDto registerUserDto)
    {
        var currentUserId = _currentUserService.GetCurrentUserId();
        var currentUser = await _userManager.FindByIdAsync(currentUserId);

        if (currentUser == null)
        {
            _logger.LogWarning("Current user not found: {UserId}", currentUserId);
            return new ServerResponse<AddUserResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "404",
                    ResponseMessage = "User not found",
                    ResponseDescription = "The current user does not exist."
                }
            };
        }

        var isSuperAdmin = await _userManager.IsInRoleAsync(currentUser, RolesConstant.SuperAdmin);

        if (!isSuperAdmin)
        {
            _logger.LogWarning("Unauthorized user registration attempt by user: {UserId}", currentUserId);
            return new ServerResponse<AddUserResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "403",
                    ResponseMessage = "Unauthorized",
                    ResponseDescription = "Only SuperAdmin can register users."
                }
            };
        }

        if (registerUserDto.Password != registerUserDto.ConfirmPassword)
        {
            return new ServerResponse<AddUserResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Password Mismatch",
                    ResponseDescription = "Passwords do not match."
                }
            };
        }

        // Validate the role
        var validRoles = new[] { RolesConstant.Creator, RolesConstant.Admin, RolesConstant.RegularUser, RolesConstant.SuperAdmin };

        if (string.IsNullOrWhiteSpace(registerUserDto.Role) || !validRoles.Contains(registerUserDto.Role.ToUpper()))
        {
            return new ServerResponse<AddUserResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Invalid Role",
                    ResponseDescription = "The selected role is invalid."
                }
            };
        }

        var role = registerUserDto.Role;

        var names = registerUserDto.FullName.Split(' ', 2);
        var firstName = names[0];
        var lastName = names.Length > 1 ? names[1] : string.Empty;

        var applicationUser = new ApplicationUser
        {
            FirstName = firstName,
            LastName = lastName,
            UserName = registerUserDto.Email,
            Email = registerUserDto.Email,
            PhoneNumber = registerUserDto.PhoneNumber,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            PublicId = Guid.NewGuid().ToString(),
        };

        var result = await _userManager.CreateAsync(applicationUser, registerUserDto.Password);
        if (!result.Succeeded)
        {
            _logger.LogError("Error occurred while creating user {UserEmail}", registerUserDto.Email);
            return new ServerResponse<AddUserResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "500",
                    ResponseMessage = "User Creation Failed",
                    ResponseDescription = string.Join(", ", result.Errors.Select(e => e.Description))
                }
            };
        }

        result = await _userManager.AddToRoleAsync(applicationUser, role);
        if (!result.Succeeded)
        {
            return new ServerResponse<AddUserResponseDto>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "500",
                    ResponseMessage = "Role Assignment Failed",
                    ResponseDescription = string.Join(", ", result.Errors.Select(e => e.Description))
                }
            };
        }

        // Add the user to the corresponding role repository
        switch (role)
        {
            case RolesConstant.Admin:
                await _adminRepository.AddAsync(new Admin
                {
                    ApplicationUserId = applicationUser.Id,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ApplicationUser = applicationUser
                });
                break;
            case RolesConstant.SuperAdmin:
                await _superAdminRepository.AddAsync(new SuperAdmin
                {
                    ApplicationUserId = applicationUser.Id,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ApplicationUser = applicationUser
                });
                break;
            case RolesConstant.Creator:
                await _creatorRepository.AddAsync(new Creator
                {
                    ApplicationUserId = applicationUser.Id,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ApplicationUser = applicationUser
                });
                break;
            case RolesConstant.RegularUser:
                await _regularUserRepository.AddAsync(new RegularUser
                {
                    ApplicationUserId = applicationUser.Id,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ApplicationUser = applicationUser
                });
                break;
        }

        var addUserResponse = new AddUserResponseDto
        {
            UserId = applicationUser.Id,
            Email = applicationUser.Email,
            Role = role,
            Message = $"{role} registration successful."
        };

        _logger.LogInformation("User {UserEmail} registered successfully as {UserRole}", applicationUser.Email, role);
        return new ServerResponse<AddUserResponseDto> { IsSuccessful = true, Data = addUserResponse };
    }

    public async Task<ServerResponse<string>> UpdateUser(UpdateUserDto updateUserDto, string userId)
    {
        _logger.LogInformation($"Attempting to update user with ID: {userId}");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning($"User with ID {userId} not found.");
            return new ServerResponse<string>
            {
                IsSuccessful = false,
                ResponseCode = "404",
                ResponseMessage = "User not found"
            };
        }

        user.FirstName = updateUserDto.firstName ?? user.FirstName;
        user.LastName = updateUserDto.lastName ?? user.LastName;
        user.Email = updateUserDto.email ?? user.Email;
        user.PhoneNumber = updateUserDto.phoneNumber ?? user.PhoneNumber;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(error => new ErrorResponse
            {
                ResponseCode = error.Code,
                ResponseMessage = error.Description
            }).ToList();

            _logger.LogError($"Failed to update user {userId}. Errors: {string.Join(", ", errors.Select(e => e.ResponseMessage))}");

            return new ServerResponse<string>
            {
                IsSuccessful = false,
                ResponseCode = "400",
                ResponseMessage = "Failed to update user",
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Update failed",
                    ResponseDescription = string.Join(", ", errors.Select(e => e.ResponseMessage))
                }
            };
        }

        _logger.LogInformation($"User with ID {userId} updated successfully.");
        return new ServerResponse<string>
        {
            IsSuccessful = true,
            ResponseCode = "200",
            ResponseMessage = "User updated successfully"
        };
    }

    public async Task<ServerResponse<string>> DeleteUser(string userId)
    {
        _logger.LogInformation($"Attempting to delete user with ID: {userId}");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning($"User with ID {userId} not found.");
            return new ServerResponse<string>
            {
                IsSuccessful = false,
                ResponseCode = "404",
                ResponseMessage = "User not found"
            };
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(error => new ErrorResponse
            {
                ResponseCode = error.Code,
                ResponseMessage = error.Description
            }).ToList();

            _logger.LogError($"Failed to delete user {userId}. Errors: {string.Join(", ", errors.Select(e => e.ResponseMessage))}");

            return new ServerResponse<string>
            {
                IsSuccessful = false,
                ResponseCode = "400",
                ResponseMessage = "Failed to delete user",
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Deletion failed",
                    ResponseDescription = string.Join(", ", errors.Select(e => e.ResponseMessage))
                }
            };
        }

        _logger.LogInformation($"User with ID {userId} deleted successfully.");
        return new ServerResponse<string>
        {
            IsSuccessful = true,
            ResponseCode = "200",
            ResponseMessage = "User deleted successfully"
        };
    }

    public async Task<ServerResponse<string>> DisableUser(string userId)
    {
        _logger.LogInformation($"Attempting to disable user with ID: {userId}");

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning($"User with ID {userId} not found.");
            return new ServerResponse<string>
            {
                IsSuccessful = false,
                ResponseCode = "404",
                ResponseMessage = "User not found"
            };
        }

        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);  // Effectively disables user

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(error => new ErrorResponse
            {
                ResponseCode = error.Code,
                ResponseMessage = error.Description
            }).ToList();

            _logger.LogError($"Failed to disable user {userId}. Errors: {string.Join(", ", errors.Select(e => e.ResponseMessage))}");

            return new ServerResponse<string>
            {
                IsSuccessful = false,
                ResponseCode = "400",
                ResponseMessage = "Failed to disable user",
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "Disable failed",
                    ResponseDescription = string.Join(", ", errors.Select(e => e.ResponseMessage))
                }
            };
        }

        _logger.LogInformation($"User with ID {userId} disabled successfully.");
        return new ServerResponse<string>
        {
            IsSuccessful = true,
            ResponseCode = "200",
            ResponseMessage = "User disabled successfully"
        };
    }

    public async Task<ServerResponse<string>> ActivateUserAsync(string userId)
    {
        // Retrieve the user by ID
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return new ServerResponse<string>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "404",
                    ResponseMessage = "User not found",
                    ResponseDescription = "The user with the provided ID does not exist."
                }
            };
        }

        // Check if the user is already active
        if (user.IsActive)
        {
            return new ServerResponse<string>
            {
                IsSuccessful = false,
                ErrorResponse = new ErrorResponse
                {
                    ResponseCode = "400",
                    ResponseMessage = "User is already active",
                    ResponseDescription = "The user is already active, no further action is required."
                }
            };
        }

        // Check if the user is a Creator
        var isCreator = await _userManager.IsInRoleAsync(user, RolesConstant.Creator);

        // If the user is a Creator and not verified, verify them
        if (isCreator && !user.IsVerified)
        {
            user.IsVerified = true;
        }
        else if (!isCreator && !user.IsVerified)
        {
            // If it's a regular user, you can decide whether you want to auto-verify them or not
            user.IsVerified = true;
        }

        // Activate the user
        user.IsActive = true;

        // Update the user in the database
        await _userManager.UpdateAsync(user);

        return new ServerResponse<string>
        {
            IsSuccessful = true,
            ResponseMessage = "User has been activated and verified (if necessary) successfully.",
            Data = "User activation complete"
        };
    }


}