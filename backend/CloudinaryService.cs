using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;


public class CloudinaryService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryService(string cloudName, string apiKey, string apiSecret)
    {
        var account = new Account(cloudName, apiKey, apiSecret);
        _cloudinary = new Cloudinary(account);
    }

    public async Task<string> UploadPrivateImageAsync(Stream imageStream, string fileName)
    {
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, imageStream),
            UseFilename = true,
            UniqueFilename = false,
            Overwrite = true,
            Folder = "doorbell_images",
            Type = "authenticated"  // This will create an authenticated URL for the image
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        if (uploadResult.StatusCode == HttpStatusCode.OK)
        {
            return uploadResult.PublicId; // Save this in DB
        }

        throw new Exception("Image upload failed.");
    }

    public class CloudinarySignedUrlGenerator
    {
        private readonly string cloudName;
        private readonly string apiKey;
        private readonly string apiSecret;

        public CloudinarySignedUrlGenerator(string cloudName, string apiKey, string apiSecret)
        {
            this.cloudName = cloudName;
            this.apiKey = apiKey;
            this.apiSecret = apiSecret;
        }

        public string GenerateAuthenticatedImageUrl(string publicId, int expiresInMinutes = 10)
        {
            var expiration = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (expiresInMinutes * 60);

            // Construct the string to sign
            var stringToSign = $"public_id={publicId}&timestamp={expiration}&type=authenticated";
            var signature = ComputeSignature(stringToSign, apiSecret);

            // Construct the full URL
            var url = $"https://res.cloudinary.com/{cloudName}/image/upload/" +
                      $"t_authenticated/{publicId}.jpg?" +
                      $"timestamp={expiration}&public_id={HttpUtility.UrlEncode(publicId)}" +
                      $"&type=authenticated&signature={signature}&api_key={apiKey}";

            return url;
        }

        private string ComputeSignature(string data, string secret)
        {
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(data + secret));
            var sb = new StringBuilder();
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

}