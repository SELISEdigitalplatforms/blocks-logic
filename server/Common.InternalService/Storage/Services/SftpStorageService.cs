using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Renci.SshNet;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class SftpStorageService : IStorageService
    {
        private readonly string _sftpHost;
        private readonly string _sftpPort;
        private readonly string _sftpUsername;
        private readonly string _sftpPassword;
        private readonly string _remoteBasePath;
        private readonly string _sftpSecretKey;
        private readonly IConfiguration _configuration;

        public SftpStorageService(IConfiguration configuration)
        {
            _sftpHost = StorageProvider.Host;
            _sftpPort = StorageProvider.Port;
            _sftpUsername = StorageProvider.UserName;
            _sftpPassword = StorageProvider.Password;
            _remoteBasePath = StorageProvider.RemoteBasePath.TrimEnd('/');
            _sftpSecretKey = StorageProvider.SftpSecretKey;
            _configuration = configuration;
        }

        private SftpClient CreateSftpClient()
        {
            int port = int.TryParse(_sftpPort, out var parsedPort) && parsedPort > 0 ? parsedPort : 22;
            return new SftpClient(_sftpHost, port, _sftpUsername, _sftpPassword);
        }

        public async Task<bool> UploadFileToSftpAsync(string fileName, string projectKey, string itemId, string versionId, IFormFile file)
        {
            try
            {
                using var client = CreateSftpClient();
                client.Connect();

                string remoteDirectory = $"{_remoteBasePath.TrimEnd('/')}/{projectKey}/{itemId}/{versionId}";
                string remoteFilePath = $"{remoteDirectory}/{fileName.TrimStart('/')}";

                EnsureRemoteDirectoryExists(client, remoteDirectory);

                using var fileStream = file.OpenReadStream();

                await Task.Run(() =>
                {
                    client.UploadFile(fileStream, remoteFilePath, true);
                });

                client.Disconnect();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Upload failed: {ex.Message}");
                return false;
            }
        }

        private static void EnsureRemoteDirectoryExists(SftpClient client, string remoteDirectory)
        {
            var parts = remoteDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPathBuilder = new StringBuilder();

            foreach (var part in parts)
            {
                currentPathBuilder.Append('/').Append(part);
                string currentPath = currentPathBuilder.ToString();
                if (!client.Exists(currentPath))
                {
                    client.CreateDirectory(currentPath);
                }
            }
        }

        public async Task<Stream?> DownloadFileAsync(string fileName, string? projectKey = null, string? itemId = null, string? versionId = null)
        {
            if (projectKey == null)
            {
                Console.WriteLine("Project key is null.");
                return null;
            }

            try
            {
                using var client = CreateSftpClient();
                client.Connect();

                string remoteFilePath = $"{_remoteBasePath.TrimEnd('/')}/{projectKey}/{itemId}/{versionId}/{fileName.TrimStart('/')}";

                var memoryStream = new MemoryStream();

                await Task.Run(() =>
                {
                    client.DownloadFile(remoteFilePath, memoryStream);
                });

                memoryStream.Position = 0; // Reset position so the caller can read

                client.Disconnect();

                return memoryStream;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Download failed: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DeleteFileAsync(string fileInfo)
        {
            try
            {
                using var client = CreateSftpClient();
                client.Connect();

                var parts = fileInfo.Split('/', StringSplitOptions.RemoveEmptyEntries);

                string? remoteDirectory = parts.Length switch
                {
                    2 => $"{_remoteBasePath.TrimEnd('/')}/{parts[0]}/{parts[1]}",
                    1 => $"{_remoteBasePath.TrimEnd('/')}/{parts[0]}",
                    _ => null
                };

                if (remoteDirectory == null)
                {
                    Console.WriteLine("Invalid fileInfo format.");
                    client.Disconnect();
                    return false;
                }

                await Task.Run(() =>
                {
                    if (client.Exists(remoteDirectory))
                    {
                        DeleteDirectoryRecursive(client, remoteDirectory);
                    }
                });

                client.Disconnect();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Delete failed: {ex.Message}");
                return false;
            }
        }

        private void DeleteDirectoryRecursive(SftpClient client, string directory)
        {
            var filesAndDirs = client.ListDirectory(directory);

            foreach (var entry in filesAndDirs)
            {
                if (entry.Name == "." || entry.Name == "..")
                    continue;

                if (entry.IsDirectory)
                {
                    DeleteDirectoryRecursive(client, entry.FullName);
                }
                else
                {
                    client.DeleteFile(entry.FullName);
                }
            }

            client.DeleteDirectory(directory);
        }

        public async Task<string?> GetDownloadUrlAsync(DownloadUrlRequest request)
        {
            request.RequestUrl = _configuration["DownloadFilesControllerUrl"];
            var expiryUtc = DateTime.UtcNow.Add(request.ExpiryDuration);
            string baseUrl = GetControllerUrl(request.RequestUrl);
            return GenerateDownloadUrl(request, baseUrl, expiryUtc);
        }

        private string GenerateDownloadUrl(DownloadUrlRequest request, string baseUrl, DateTime expiryUtc)
        {
            SignatureString signatureString = new SignatureString
            {
                ItemId = request.ItemId,
                FileVersion = request.FileVersion,
                ConfiguratioName = request.ConfigurationName,
                AccessModifier = request.AccessModifier.ToString(),
                ProjectKey = request.ProjectKey,
                ExpiryUtc = expiryUtc.ToString("o", CultureInfo.InvariantCulture)
            };

            string raw = JsonSerializer.Serialize(signatureString);

            string signature = AesEncryptionHelper.Encrypt(raw, _sftpSecretKey);

            var queryParams = new List<string>
            {
                $"projectkey={Uri.EscapeDataString(request.ProjectKey ?? string.Empty)}",
                $"x-blocks-key={Uri.EscapeDataString(request.ProjectKey ?? string.Empty)}",
                $"configuratioName={Uri.EscapeDataString(request.ConfigurationName ?? string.Empty)}",
                $"signature={Uri.EscapeDataString(signature)}"
            };

            return $"{baseUrl}/downloadfile?{string.Join("&", queryParams)}";
        }

        private static string GetControllerUrl(string? fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return string.Empty;

            var uri = new Uri(fullPath);
            var segments = uri.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length > 1)
            {
                string trimmedPath = $"/{string.Join('/', segments.Take(segments.Length - 1))}";
                return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}{trimmedPath}";
            }

            return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}{uri.AbsolutePath}";
        }

        public Task<IEnumerable<string>> ListFilesAsync()
        {
            throw new NotImplementedException();
        }

        public string GeneratePreSignedUploadUrlAsync(string fileName, TimeSpan expiry)
        {
            throw new NotImplementedException();
        }
    }
}
