﻿
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Lyvads.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Lyvads.Application.Implementations;

public class MediaService : IMediaService
{
    private readonly Cloudinary cloudinary;

    public MediaService(IConfiguration config)
    {
        var cloudName = config.GetSection("Cloudinary:CloudName").Value;
        var apiKey = config.GetSection("Cloudinary:ApiKey").Value;
        var apiSecret = config.GetSection("Cloudinary:ApiSecret").Value;

        Account account = new Account
        {
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            Cloud = cloudName
        };

        cloudinary = new Cloudinary(account);
    }

    public async Task<Dictionary<string, string>> UploadImageAsync(IFormFile photo, string folderName)
    {
        var response = new Dictionary<string, string>();
        var defaultSize = 500000;
        var allowedTypes = new List<string>() { "jpeg", "jpg", "png" };
        Console.WriteLine($"Allowed Types: {string.Join(", ", allowedTypes)}");


        if (photo == null)
        {
            response.Add("Code", "400");
            response.Add("Message", "No file uploaded");
            return response;
        }

        var file = photo;

        if (file.Length < 1 || file.Length > defaultSize)
        {
            response.Add("Code", "400");
            response.Add("Message", "Invalid size");
            return response;
        }

        if (allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            response.Add("Code", "400");
            response.Add("Message", "Invalid type");
            return response;
        }

        var uploadResult = new ImageUploadResult();

        using (var stream = file.OpenReadStream())
        {
            var uploadParams = new ImageUploadParams()
            {
                File = new FileDescription(file.Name, stream),
                Transformation = new Transformation().Width(500).Height(500).Crop("fill").Gravity("face"),
                Folder = folderName
            };
            uploadResult = await cloudinary.UploadAsync(uploadParams);
        }

        if (!string.IsNullOrEmpty(uploadResult.PublicId))
        {
            response.Add("Code", "200");
            response.Add("Message", "Upload successful");
            response.Add("PublicId", uploadResult.PublicId);
            response.Add("Url", uploadResult.Url.ToString());

            return response;
        }

        response.Add("Code", "400");
        response.Add("Message", "Failed to upload");
        return response;
    }

    public async Task<DeletionResult> DeleteImageAsync(string publicUrl)
    {
        var publicId = publicUrl.Split('/').Last().Split('.')[0];
        var deleteParams = new DeletionParams(publicId);
        return await cloudinary.DestroyAsync(deleteParams);
    }

    public async Task<Dictionary<string, string>> UploadVideoAsync(IFormFile video, string folderName)
    {
        var response = new Dictionary<string, string>();
        var defaultSize = 10000000; // Example size limit for videos
        var allowedTypes = new List<string>() { "mp4", "avi", "mov" }; // Example allowed video types
        Console.WriteLine($"Allowed Types: {string.Join(", ", allowedTypes)}");

        if (video == null)
        {
            response.Add("Code", "400");
            response.Add("Message", "No file uploaded");
            return response;
        }

        var file = video;

        if (file.Length < 1 || file.Length > defaultSize)
        {
            response.Add("Code", "400");
            response.Add("Message", "Invalid size");
            return response;
        }

        if (!allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            response.Add("Code", "400");
            response.Add("Message", "Invalid type");
            return response;
        }

        var uploadResult = new VideoUploadResult(); // Assuming a separate result class for videos

        using (var stream = file.OpenReadStream())
        {
            var uploadParams = new VideoUploadParams()
            {
                File = new FileDescription(file.Name, stream),
                Folder = folderName
                // Additional video-specific parameters can be added here
            };
            uploadResult = await cloudinary.UploadAsync(uploadParams); // Assuming cloudinary handles video uploads similarly
        }

        if (!string.IsNullOrEmpty(uploadResult.PublicId))
        {
            response.Add("Code", "200");
            response.Add("Message", "Upload successful");
            response.Add("PublicId", uploadResult.PublicId);
            response.Add("Url", uploadResult.Url.ToString());

            return response;
        }

        response.Add("Code", "400");
        response.Add("Message", "Failed to upload");
        return response;
    }

    public async Task<DeletionResult> DeleteVideoAsync(string publicUrl)
    {
        var publicId = publicUrl.Split('/').Last().Split('.')[0];
        var deleteParams = new DeletionParams(publicId);

        // Assuming your cloud provider uses the same API for deleting videos
        return await cloudinary.DestroyAsync(deleteParams);
    }

    public async Task<Dictionary<string, string>> GetDownloadUrlAsync(string publicId, string folderName)
    {
        var response = new Dictionary<string, string>();

        // Construct the Cloudinary URL for the video
        var downloadUrl = cloudinary.Api.UrlVideoUp
                            .Action("download")
                            .ResourceType("video")
                            .Secure(true)
                            .Transform(new Transformation().Quality("auto"))
                            .BuildUrl($"{folderName}/{publicId}.mp4"); // Assuming the file is in mp4 format

        if (!string.IsNullOrEmpty(downloadUrl))
        {
            response.Add("Code", "200");
            response.Add("Message", "Download URL generated successfully");
            response.Add("DownloadUrl", downloadUrl);
        }
        else
        {
            response.Add("Code", "400");
            response.Add("Message", "Failed to generate download URL");
        }

        return response;
    }

}