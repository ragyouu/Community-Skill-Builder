namespace SkillBuilder.Services
{
    public interface ICloudinaryService
    {
        Task<string?> UploadImageAsync(IFormFile file, string folder);
        Task<string?> UploadVideoAsync(IFormFile file, string folder);
        Task<bool> DeleteImageAsync(string publicId);
        Task<bool> DeleteVideoAsync(string publicId);
    }
}
