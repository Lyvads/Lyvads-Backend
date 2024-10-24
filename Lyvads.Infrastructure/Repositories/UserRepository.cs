﻿using Lyvads.Infrastructure.Persistence;
using Lyvads.Domain.Entities;
using Lyvads.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lyvads.Shared.DTOs;

namespace Lyvads.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(AppDbContext context, ILogger<UserRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApplicationUser> GetUserByIdAsync(string userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            throw new Exception("User not found");
        }
        return user;
    }

    public async Task AddCommentAsync(Comment comment)
    {
        await _context.Comments.AddAsync(comment);
        await _context.SaveChangesAsync();
    }

    public async Task AddLikeAsync(Like like)
    {
        await _context.Likes.AddAsync(like);
        await _context.SaveChangesAsync();
    }

    public async Task<Like> GetLikeAsync(string userId, string contentId)
    {
        var like = await _context.Likes
            .FirstOrDefaultAsync(l => l.UserId == userId && l.ContentId == contentId);

        if (like == null)
        {
            throw new Exception("Like not found");
        }

        return like;
    }


    public async Task RemoveLikeAsync(Like like)
    {
        _context.Likes.Remove(like);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateWalletBalanceAsync(string userId, decimal amount)
    {
        var user = await _context.Users.Include(u => u.Wallet).FirstOrDefaultAsync(u => u.Id == userId); // Ensure the Wallet is included
        if (user != null)
        {
            // Check if Wallet is null and initialize if necessary
            if (user.Wallet == null)
            {
                user.Wallet = new Wallet
                {
                    Balance = 0 // Initialize balance if it was null
                };
            }

            // Update the balance
            user.Wallet.Balance += amount;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }
        else
        {
            // Handle case when user is not found
            throw new Exception($"User with ID {userId} not found.");
        }
    }


    public async Task AddFavoriteAsync(string userId, string creatorId)
    {
        var favorite = new Favorite { UserId = userId, CreatorId = creatorId };
        await _context.Favorites.AddAsync(favorite);
        await _context.SaveChangesAsync();
    }

    public async Task FollowCreatorAsync(string userId, string creatorId)
    {
        var follow = new Follow { UserId = userId, CreatorId = creatorId };
        await _context.Follows.AddAsync(follow);
        await _context.SaveChangesAsync();
    }

    public async Task<CreatorProfileDto> GetCreatorByIdAsync(string creatorId)
    {
        var creator = await _context.Creators
            .Include(c => c.CollabRates) // Ensure CollabRates is a collection of CollaborationRate entities
            .FirstOrDefaultAsync(c => c.Id == creatorId);

        if (creator == null) return null!;

        // Convert the CollaborationRate entities to CollabRateDto
        var collabRatesDto = creator.CollabRates?
            .Select(cr => new CollabRateDto
            {
                RequestType = cr.RequestType.ToString(), // Assuming RequestType is a string
                TotalAmount = cr.Rate // Assuming Rate is the amount
            })
            .ToList();

        return new CreatorProfileDto
        {
            Id = creator.Id,
            Name = creator.ApplicationUser?.FullName,
            EngagementCount = creator.EngagementCount,
            CollabRates = collabRatesDto // Return the converted DTO list
        };
    }


    //public async Task<Creator?> GetCreatorByIdAsync(string creatorId)
    //{
    //    return await _context.Creators
    //        .Include(c => c.CollabRates) // Make sure to include CollabRates
    //        .FirstOrDefaultAsync(c => c.Id == creatorId);
    //}

    public async Task<Favorite?> GetFavoriteAsync(string userId, string creatorId)
    {
        return await _context.Favorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.CreatorId == creatorId);
    }


    //public async Task<List<CreatorResponseDto>> GetCreatorsWithMostEngagementAndFollowersAsync(int count)
    //{
    //    return await _context.Creators
    //        .Select(c => new CreatorResponseDto
    //        {
    //            Id = c.Id,
    //            // Use a conditional expression to handle null values explicitly
    //            Name = c.ApplicationUser != null ? c.ApplicationUser.FullName! : "Unknown",
    //            EngagementCount = c.Posts.Sum(p => p.Likes.Count + p.Comments.Count),
    //            FollowersCount = _context.Follows.Count(f => f.CreatorId == c.Id) // Count followers from the Follow entity
    //        })
    //        .OrderByDescending(c => c.FollowersCount) // Sort by FollowerCount
    //        .ThenByDescending(c => c.EngagementCount) // Sort by EngagementCount
    //        .Take(count)
    //        .ToListAsync();
    //}

    public IQueryable<CreatorResponseDto> GetCreatorsWithMostEngagementAndFollowersAsync()
    {
        // Precompute EngagementCount and FollowersCount before projection
        return _context.Creators
            .Select(c => new
            {
                c.Id,
                Name = c.ApplicationUser != null ? c.ApplicationUser.FullName! : "Unknown",
                EngagementCount = c.Posts.SelectMany(p => p.Likes).Count() + c.Posts.SelectMany(p => p.Comments).Count(), // Flatten Likes and Comments first, then count
                FollowersCount = _context.Follows.Count(f => f.CreatorId == c.Id) // Count followers
            })
            .OrderByDescending(c => c.FollowersCount)
            .ThenByDescending(c => c.EngagementCount)
            .Select(c => new CreatorResponseDto
            {
                Id = c.Id,
                Name = c.Name,
                EngagementCount = c.EngagementCount,
                FollowersCount = c.FollowersCount
            });
    }


    public async Task<List<CreatorResponseDto>> GetFavoriteCreatorsAsync(string userId)
    {
        var favorites = await _context.Favorites
            .Where(f => f.UserId == userId)
            .Select(f => f.Creator)
            .ToListAsync();

        return favorites.Select(c => new CreatorResponseDto
        {
            Id = c!.Id,
            Name = c.ApplicationUser?.FullName!,
            EngagementCount = c.EngagementCount,
            // Add other relevant properties
        }).ToList();
    }

    public async Task RemoveFavoriteAsync(string userId, string creatorId)
    {
        // Fetch the favorite entry from the database
        var favorite = await _context.Favorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.CreatorId == creatorId);

        if (favorite != null)
        {
            // Remove the favorite entry
            _context.Favorites.Remove(favorite);

            // Save changes to the database
            await _context.SaveChangesAsync();
        }
    }


    public async Task<int> GetFollowerCountAsync(string creatorId)
    {
        return await _context.Follows.CountAsync(f => f.CreatorId == creatorId);
    }

    public async Task<int> GetLikesCountAsync(string postId)
    {
        return await _context.Likes.CountAsync(l => l.PostId == postId);
    }

    public async Task<List<string>> GetUserIdsWhoLikedPostAsync(string postId)
    {
        return await _context.Likes
            .Where(l => l.PostId == postId)
            .Join(_context.Users, // Join with the Users table
                  like => like.UserId, // Foreign key in Likes
                  user => user.Id, // Primary key in Users
                  (like, user) => user.FullName!) // Project to the user's FullName
            .ToListAsync();
    }


    public async Task UnfollowCreatorAsync(string userId, string creatorId)
    {
        var follow = await _context.Follows
            .FirstOrDefaultAsync(f => f.UserId == userId && f.CreatorId == creatorId);
        if (follow != null)
        {
            _context.Follows.Remove(follow);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> IsUserFollowingCreatorAsync(string userId, string creatorId)
    {
        return await _context.Follows
            .AnyAsync(f => f.UserId == userId && f.CreatorId == creatorId);
    }
}
