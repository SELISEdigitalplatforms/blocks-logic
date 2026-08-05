using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using FluentValidation;
using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using File = Common.InternalService.Storage.File;

namespace Common.InternalService.Storage
{
    [ExcludeFromCodeCoverage]
    public class FileManagementService : IFileManagementService
    {
        private readonly IFileRepository _fileRepository;
        private readonly IFileVersionRepository _versionRepository;
        private readonly IStorageServiceFactory _storageServiceFactory;
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IConfigurationRepository _configurationRepository;
        private readonly IValidator<GetPreSignedUrlForUploadRequest> _requestValidator;
        private readonly IContentAccessResolver _accessResolver;
        private readonly IContentAccessRepository _accessRepository;

        private const string ConfigurationNotFound = "configuration_not_found";

        public FileManagementService(
            IFileRepository fileRepository,
            IStorageServiceFactory storageServiceFactory,
            IFileVersionRepository versionRepository,
            IConfigurationRepository configurationRepository,
            IDirectoryRepository directoryRepository,
            IValidator<GetPreSignedUrlForUploadRequest> requestValidator,
            IContentAccessResolver accessResolver,
            IContentAccessRepository accessRepository
            )
        {
            _fileRepository = fileRepository;
            _storageServiceFactory = storageServiceFactory;
            _versionRepository = versionRepository;
            _configurationRepository = configurationRepository;
            _directoryRepository = directoryRepository;
            _requestValidator = requestValidator;
            _accessResolver = accessResolver;
            _accessRepository = accessRepository;
        }

        public async Task<GetPreSignedUrlForUploadResponse> GetPerSignedUrlForUploadAsync(GetPreSignedUrlForUploadRequest request)
        {
            var validationResult = await ValidateRequestAsync(request);

            if (!validationResult.IsSuccess)
                return validationResult;

            request.ItemId = string.IsNullOrEmpty(request.ItemId) ? Guid.NewGuid().ToString() : request.ItemId;
            var existingFile = await _fileRepository.GetFileByItemIdAsync(request.ItemId);
            if (existingFile is not null && !await AuthorizeFileAsync(existingFile, ContentPermission.Edit, "Upload", default))
                return AccessDenied<GetPreSignedUrlForUploadResponse>();
            if (existingFile is null && !await AuthorizeParentEditAsync(request.ParentDirectoryId, "Upload", default))
                return AccessDenied<GetPreSignedUrlForUploadResponse>();
            return existingFile != null
                ? await HandleExistingFileAsync(request, existingFile)
                : await HandleNewFileAsync(request);
        }

        private async Task<GetPreSignedUrlForUploadResponse> ValidateRequestAsync(GetPreSignedUrlForUploadRequest request)
        {
            var validationResult = await _requestValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return new GetPreSignedUrlForUploadResponse
                {
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage),
                    IsSuccess = false
                };
            }

            if (!Path.HasExtension(request.Name))
            {
                return new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = "File name does not have any extension",
                    FileId = request.ItemId,
                    IsSuccess = false
                };
            }

            var fileExtension = Path.GetExtension(request.Name).ToLower();

            if (UnsupportedFile.Extensions.Contains(fileExtension))
            {
                return new GetPreSignedUrlForUploadResponse
                {
                    UploadUrl = $"File extension {fileExtension} is not supported",
                    FileId = request.ItemId,
                    IsSuccess = false
                };
            }

            if (!string.IsNullOrEmpty(request.ParentDirectoryId))
            {
                var directory = await _directoryRepository.GetDirectoryByItemIDAsync(request.ParentDirectoryId);
                if (directory?.AllowedFileExtensions?.Any() == true && !directory.AllowedFileExtensions.Contains(fileExtension))
                {
                    return new GetPreSignedUrlForUploadResponse
                    {
                        UploadUrl = $"File extension {fileExtension} is not supported for this directory",
                        FileId = request.ItemId,
                        IsSuccess = false
                    };
                }
            }

            return new GetPreSignedUrlForUploadResponse { IsSuccess = true };
        }

        private async Task<GetPreSignedUrlForUploadResponse> HandleExistingFileAsync(GetPreSignedUrlForUploadRequest request, File existingFile)
        {
            var latestFileVersionNumber = await _versionRepository.GetLatestFileVersionNumberAsync(existingFile.ItemId);
            var newFileVersion = CreateNewFileVersion(existingFile.ItemId, latestFileVersionNumber);

            var configuration = await GetConfigurationAsync(request.ConfigurationName);

            if (configuration == null)
            {
                return CreateErrorResponse<GetPreSignedUrlForUploadResponse>("Configuration", ConfigurationNotFound);
            }

            var storageServiceProvider = GetStorageService(configuration);

            var fileInfo = GetFileInfo(existingFile.ItemId, newFileVersion.ItemId, existingFile.Name, existingFile.AccessModifier, StorageStrategyCategory.Cloud);
            newFileVersion.StorageKey = fileInfo.filePath;
            var preSignedUrl = storageServiceProvider.GeneratePreSignedUploadUrlAsync(fileInfo.filePath, fileInfo.expiry);

            await Task.WhenAll(_versionRepository.CreateFileVersionAsync(newFileVersion));

            return new GetPreSignedUrlForUploadResponse
            {
                UploadUrl = preSignedUrl,
                FileId = existingFile.ItemId,
                IsSuccess = true
            };
        }

        private async Task<GetPreSignedUrlForUploadResponse> HandleNewFileAsync(GetPreSignedUrlForUploadRequest request)
        {
            var configuration = await GetConfigurationAsync(request.ConfigurationName);

            if (configuration == null)
            {
                return CreateErrorResponse<GetPreSignedUrlForUploadResponse>("Configuration", ConfigurationNotFound);
            }

            var file = await CreateNewFileAsync(request);
            file.ConfigurationName = configuration.Name;
            var fileVersion = CreateNewFileVersion(file.ItemId, 0);

            var storageServiceProvider = GetStorageService(configuration);

            var fileInfo = GetFileInfo(file.ItemId, fileVersion.ItemId, file.Name, file.AccessModifier, StorageStrategyCategory.Cloud);
            fileVersion.StorageKey = fileInfo.filePath;
            var preSignedUrl = storageServiceProvider.GeneratePreSignedUploadUrlAsync(fileInfo.filePath, TimeSpan.FromDays(3));

            file.Url = preSignedUrl;

            await Task.WhenAll(_fileRepository.CreateFileAsync(file),
                               _versionRepository.CreateFileVersionAsync(fileVersion));

            return new GetPreSignedUrlForUploadResponse
            {
                UploadUrl = preSignedUrl,
                FileId = file.ItemId,
                IsSuccess = true
            };
        }

        private const string DefaultConfigurationName = "Default";

        private async Task<StorageConfiguration> GetConfigurationAsync(string? configurationName)
        {
            return await _configurationRepository.GetStorageConfigurationByNameAsync(configurationName ?? DefaultConfigurationName);
        }

        private IStorageService GetStorageService(StorageConfiguration configuration)
        {
            return _storageServiceFactory.GetStorageService(configuration);
        }

        /// <summary>
        /// Builds a new file with the parent's cached ancestry. This keeps a freshly
        /// uploaded file in the same access-inheritance chain as its directory from its
        /// first read; previously it stayed at an empty ancestry until a later rebuild.
        /// </summary>
        private async Task<File> CreateNewFileAsync(dynamic request)
        {
            var now = DateTime.UtcNow;
            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;

            var tags = ParseTags(request.Tags);

            var meta = string.IsNullOrWhiteSpace(request.MetaData)
                ? new Dictionary<string, MetaValue>()
                : JsonSerializer.Deserialize<Dictionary<string, MetaValue>>(request.MetaData) ?? new Dictionary<string, MetaValue>();

            var parentId = string.IsNullOrWhiteSpace(request.ParentDirectoryId)
                ? null
                : (string)request.ParentDirectoryId;
            var parent = parentId is null ? null : await _directoryRepository.GetDirectoryByItemIDAsync(parentId);
            var ancestorIds = parent is null
                ? new List<string>()
                : (parent.AncestorIds ?? new List<string>()).Concat(new[] { parent.ItemId }).ToList();

            return new File
            {
                Name = request.Name,
                DirectoryId = parentId ?? string.Empty,
                SystemName = request.Name.ToLower(),
                Type = StructureType.File,
                TypeString = StructureType.File.ToString(),
                MetaData = meta,
                Url = string.Empty,
                ItemId = request.ItemId,
                TenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty,
                CreatedDate = now,
                CreatedBy = userId,
                // A fresh upload is also the initial version of this file, not an
                // update after creation. Persist the same timestamp for both fields.
                LastUpdatedDate = now,
                LastUpdatedBy = userId,
                Tags = tags,
                Language = "EN",
                AccessModifier = string.IsNullOrWhiteSpace(request.AccessModifier)
                    ? AccessModifier.Private
                    : Enum.Parse<AccessModifier>(request.AccessModifier),
                CurrentVersion = 0,
                AncestorIds = ancestorIds,
                InheritsParentAccess = true,
                Extension = Path.GetExtension((string)request.Name).TrimStart('.'),
                ConfigurationName = request.ConfigurationName,
                AdditionalProperties = request.AdditionalProperties ?? new Dictionary<string, string>(),
            };
        }

        private static List<string> ParseTags(string? tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
                return new List<string>();

            var trimmedTags = tags.Trim();

            // Upload clients historically send a JSON array (for example, ["tag-1"]),
            // while some callers provide one tag as plain text. Treat plain text as one
            // tag instead of attempting to deserialize it as JSON.
            if (!trimmedTags.StartsWith("[", StringComparison.Ordinal))
                return new List<string> { trimmedTags };

            return JsonSerializer.Deserialize<List<string>>(trimmedTags) ?? new List<string>();
        }

        private FileVersion CreateNewFileVersion(string fileId, long versionNumber)
        {
            return FileVersion.CreateNew(
                fileId,
                versionNumber,
                new FileVersionOptions
                {
                    ItemId = Guid.NewGuid().ToString(),
                    TenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty,
                    CreateDate = DateTime.UtcNow,
                    CreatedBy = BlocksContext.GetContext()?.UserId ?? string.Empty,
                    UploadedBy = BlocksContext.GetContext()?.UserId ?? string.Empty,
                    Tags = null,
                    Language = "EN"
                });
        }

        public async Task<FileResponse?> GetUrlForDownloadFileAsync(GetFileRequest request)
        {
            if (string.IsNullOrEmpty(request.FileId))
            {
                return CreateErrorResponse<FileResponse>("empty_file_id", "file_id_should_not_be_empty");
            }

            var result = _fileRepository.GetRequiredFiles([request.FileId], request.Version);
            var file = await _fileRepository.GetFileByItemIdAsync(request.FileId);
            if (file is null || !await AuthorizeFileAsync(file, ContentPermission.Download, "Download", default))
                return AccessDenied<FileResponse>();
            var configuration = await GetConfigurationAsync(request.ConfigurationName);

            if (configuration == null)
            {
                return CreateErrorResponse<FileResponse>("configuration", ConfigurationNotFound);
            }
            var context = BlocksContext.GetContext();
            var tenantId = context?.TenantId ?? string.Empty;

            var finalFileResponse = await GetFileResponse(result.Item1, result.Item2, configuration, tenantId);

            return finalFileResponse?.FirstOrDefault();
        }

        public async Task<List<FileResponse>?> GetMultipleUrlsForDownloadFilesAsync(GetFilesRequest request)
        {
            List<FileResponse>? finalfileResponse = new List<FileResponse>();

            if (!request.FileIds.Any())
            {
                finalfileResponse.Add(CreateErrorResponse<FileResponse>("empty_file_id", "file_id_should_not_be_empty"));
                return finalfileResponse;
            }

            foreach (var fileId in request.FileIds)
            {
                var file = await _fileRepository.GetFileByItemIdAsync(fileId);
                if (file is null || !await AuthorizeFileAsync(file, ContentPermission.Download, "Download", default))
                    return new List<FileResponse> { AccessDenied<FileResponse>() };
            }

            var result = _fileRepository.GetRequiredFiles(request.FileIds, null);
            var configuration = await GetConfigurationAsync(request.ConfigurationName);

            if (configuration == null)
            {
                finalfileResponse.Add(CreateErrorResponse<FileResponse>("configuration", ConfigurationNotFound));
                return finalfileResponse;
            }
            var context = BlocksContext.GetContext();
            var tenantId = context?.TenantId ?? string.Empty;

            return await GetFileResponse(result.Item1, result.Item2, configuration, tenantId);
        }

        private async Task<List<FileResponse>?> GetFileResponse(IEnumerable<BsonDocument> bsonElements, FileResponse[] responses, StorageConfiguration configuration, string? projectKey)
        {
            List<FileResponse>? finalfileResponse = new List<FileResponse>();

            foreach (var fileVersionAggregate in bsonElements)
            {
                if (!fileVersionAggregate.Any()) { continue; }

                var fileId = fileVersionAggregate["_id"].AsString;
                var latestVersion = fileVersionAggregate["VersionId"].AsString;
                var latestVersionNo = fileVersionAggregate["MaxVersion"].IsBsonNull ? 0 : fileVersionAggregate["MaxVersion"].AsInt64;
                var fileResponse = responses.First(f => f.ItemId.Equals(fileId));

                var fileUrlResponse = await GetFileUrlResponse(configuration, projectKey, fileResponse, latestVersionNo, latestVersion);

                if (fileUrlResponse.Errors != null)
                {
                    finalfileResponse.Add(fileUrlResponse);
                    return finalfileResponse;
                }

                fileResponse.Url = fileUrlResponse.Url;

                fileResponse.SizeInBytes = fileVersionAggregate["SizeInBytes"].IsBsonNull ? 0 : fileVersionAggregate["SizeInBytes"].AsInt64;
                fileResponse.IsSuccess = true;

                finalfileResponse.Add(fileResponse);
            }

            return finalfileResponse;
        }

        private async Task<FileResponse> GetFileUrlResponse(StorageConfiguration configuration, string? projectKey, FileResponse fileResponse, long latestVersionNo, string latestVersion)
        {
            var storageServiceProvider = _storageServiceFactory.GetStorageService(configuration);

            DownloadUrlRequest fileUrlRequest = new DownloadUrlRequest
            {
                ItemId = fileResponse.ItemId,
                FileVersion = latestVersionNo,
                ConfigurationName = configuration.Name,
                ProjectKey = projectKey ?? BlocksContext.GetContext().TenantId,
                AccessModifier = fileResponse.AccessModifier
            };

            _ = StorageTypes.TryGetCategory(configuration.StorageStrategy, out var category);
            var fileInfo = GetFileInfo(fileResponse.ItemId, latestVersion, fileResponse.Name, fileResponse.AccessModifier, category);

            fileUrlRequest.FileName = fileInfo.filePath;
            fileUrlRequest.ExpiryDuration = fileInfo.expiry;

            fileResponse.Url = await storageServiceProvider.GetDownloadUrlAsync(fileUrlRequest) ?? "";
            return fileResponse;
        }

        private static (string filePath, TimeSpan expiry) GetFileInfo(string fileId, string fileVersionId, string fileName, AccessModifier accessModifier, StorageStrategyCategory category)
        {
            switch (category)
            {
                case StorageStrategyCategory.Local:
                    return ("", accessModifier == AccessModifier.Private ? TimeSpan.FromMinutes(30) : TimeSpan.Zero);

                default:
                    {
                        var filePath = accessModifier == AccessModifier.Public
                            ? $"Public/{fileId}/{fileVersionId}/{fileName}"
                            : $"Private/{fileId}/{fileVersionId}/{fileName}";
                        return (filePath, TimeSpan.FromDays(3));
                    }
            }
        }

        private T CreateErrorResponse<T>(string fieldName, string errorMessage) where T : BaseResponse, new()
        {
            return new T
            {
                Errors = new Dictionary<string, string> { { fieldName, errorMessage } },
                IsSuccess = false
            };
        }

        private async Task<bool> AuthorizeParentEditAsync(string? directoryId, string action, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(directoryId)) return true; // root upload remains IAM-gated.
            var directory = await _directoryRepository.GetDirectoryByItemIDAsync(directoryId);
            if (directory is null) return false;
            return await AuthorizeAsync(new ContentResourceDescriptor
            {
                ResourceId = directory.ItemId, AncestorIds = directory.AncestorIds ?? new(),
                InheritsParentAccess = directory.InheritsParentAccess, CreatedBy = directory.CreatedBy,
            }, ContentResourceType.Directory, ContentPermission.Edit, action, cancellationToken);
        }

        private Task<bool> AuthorizeFileAsync(File file, ContentPermission permission, string action, CancellationToken cancellationToken) =>
            AuthorizeAsync(new ContentResourceDescriptor
            {
                ResourceId = file.ItemId, AncestorIds = file.AncestorIds ?? new(),
                InheritsParentAccess = file.InheritsParentAccess, CreatedBy = file.CreatedBy,
            }, ContentResourceType.File, permission, action, cancellationToken);

        private async Task<bool> AuthorizeAsync(ContentResourceDescriptor resource, ContentResourceType resourceType,
            ContentPermission permission, string action, CancellationToken cancellationToken)
        {
            var granted = await _accessResolver.ResolveAsync(resource, permission, cancellationToken);
            var context = BlocksContext.GetContext();
            var userId = context?.UserId ?? string.Empty;
            await _accessRepository.WriteAuditAsync(new ContentAuditLog
            {
                ItemId = Guid.NewGuid().ToString(), TenantId = context?.TenantId ?? string.Empty,
                ResourceId = resource.ResourceId, ResourceType = resourceType, UserId = userId,
                Action = action, Granted = granted, Detail = permission.ToString(),
                CreatedDate = DateTime.UtcNow, CreatedBy = userId,
            }, cancellationToken);
            return granted;
        }

        private static T AccessDenied<T>() where T : BaseResponse, new() => new()
        {
            IsSuccess = false,
            Errors = new Dictionary<string, string> { { "access", "forbidden" } },
        };
    }
}
