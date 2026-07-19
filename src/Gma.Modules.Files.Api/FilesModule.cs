namespace Gma.Modules.Files.Api;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using Gma.Modules.Files.Application;
using Gma.Modules.Files.Application.Commands;
using Gma.Modules.Files.Application.Queries;
using Gma.Modules.Files.Application.ReadModels;
using Gma.Modules.Files.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Gma.Framework.AccessControl;
using Gma.Framework.Api.Modules;
using Gma.Framework.Api.Observability;
using Gma.Framework.Api.Scoping;
using Gma.Framework.Api.Results;
using Gma.Framework.Cqrs;
using Gma.Framework.FileManagement;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Naming;
using Gma.Framework.Scoping;
using Gma.Framework.Results;
using Gma.Framework.Security;
using Microsoft.Net.Http.Headers;

public sealed class FilesModule : IModule
{
    public string Name => FilesModuleMetadata.Name;

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.SelectModuleProfile(FilesProfiles.Default, "Gma.Modules.Files.Api");
        builder.Services.AddFilesApplication(builder.Configuration);
        ConfigureMultipartLimits(builder);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/files")
            .WithModuleName(this.Name)
            .WithTags("Files")
            .RequireAuthorization();

        group.MapPost("/", async (
            IFormFile? file,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserSubject(httpContext, scopeContext, out AccessSubject? subject, out IResult? failure))
            {
                return failure;
            }

            if (file is null)
            {
                return Result.Failure<FileUploadResponse>(FilesApplicationErrors.FileRequired)
                    .ToHttpResult(PublicErrorStatusCodes);
            }

            await using Stream stream = file.OpenReadStream();
            Result<FileUploadResponse> result = await dispatcher.SendAsync(
                new UploadFileCommand(stream, file.Length, file.ContentType, file.FileName, subject),
                cancellationToken).ConfigureAwait(false);

            return result.IsFailure
                ? result.ToHttpResult(PublicErrorStatusCodes)
                : Results.Created(result.Value.DownloadPath, result.Value);
        })
            .RequireScope()
            .RequireAuthorization()
            .DisableAntiforgery();

        group.MapGet("/{fileId:guid}", async (
            Guid fileId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserSubject(httpContext, scopeContext, out AccessSubject? subject, out IResult? failure))
            {
                return failure;
            }

            Result<FileDownload> result = await dispatcher.QueryAsync(
                new GetFileQuery(fileId, subject),
                cancellationToken).ConfigureAwait(false);

            return result.IsFailure
                ? result.ToHttpResult(PublicErrorStatusCodes)
                : new FileDownloadHttpResult(result.Value);
        })
            .RequireScope()
            .RequireAuthorization();

        group.MapDelete("/{fileId:guid}", async (
            Guid fileId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserSubject(httpContext, scopeContext, out AccessSubject? subject, out IResult? failure))
            {
                return failure;
            }

            Result<Unit> result = await dispatcher.SendAsync(
                new DeleteFileCommand(fileId, subject),
                cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope()
            .RequireAuthorization();
    }

    private static readonly ApiErrorStatusCodeMap PublicErrorStatusCodes = ApiErrorStatusCodeMap.Create(
        new(FilesApplicationErrors.ScopeRequired.Code, StatusCodes.Status400BadRequest),
        new(FilesApplicationErrors.FileRequired.Code, StatusCodes.Status400BadRequest),
        new(FilesApplicationErrors.FileEmpty.Code, StatusCodes.Status400BadRequest),
        new(FilesApplicationErrors.FileTooLarge.Code, StatusCodes.Status413PayloadTooLarge),
        new(FilesApplicationErrors.ContentTypeNotAllowed.Code, StatusCodes.Status415UnsupportedMediaType),
        new(FilesApplicationErrors.ContentInspectionRequired.Code, StatusCodes.Status503ServiceUnavailable),
        new(FilesApplicationErrors.ContentRejected.Code, StatusCodes.Status422UnprocessableEntity),
        new(FilesApplicationErrors.ContentLengthMismatch.Code, StatusCodes.Status400BadRequest),
        new(FilesApplicationErrors.FileIdInvalid.Code, StatusCodes.Status400BadRequest),
        new(FilesApplicationErrors.FileNotFound.Code, StatusCodes.Status404NotFound),
        new(FilesApplicationErrors.AccessDenied.Code, StatusCodes.Status403Forbidden));

    private static bool TryResolveUserSubject(
        HttpContext httpContext,
        IScopeContext scopeContext,
        [NotNullWhen(true)] out AccessSubject? subject,
        [NotNullWhen(false)] out IResult? failure)
    {
        subject = null;
        failure = null;

        string? userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                         httpContext.User.FindFirstValue(ApplicationClaimNames.Subject);
        if (string.IsNullOrWhiteSpace(userId))
        {
            failure = Results.Unauthorized();
            return false;
        }

        if (scopeContext.IsEnabled)
        {
            string? tokenScopeId = httpContext.User.FindFirstValue(ApplicationClaimNames.ScopeId);
            if (!ScopeIds.TryNormalize(tokenScopeId, out string? normalizedTokenScopeId) ||
                !string.Equals(normalizedTokenScopeId, scopeContext.ScopeId, StringComparison.Ordinal))
            {
                failure = Results.Forbid();
                return false;
            }
        }

        if (!AccessSubject.TryCreate(AccessSubjectKind.User, userId, out subject))
        {
            failure = Results.Unauthorized();
            return false;
        }

        return true;
    }

    private static void ConfigureMultipartLimits(IHostApplicationBuilder builder)
    {
        string? configuredMaximum = builder.Configuration[
            $"{FileManagementOptions.SectionName}:MaximumObjectBytes"];
        long maximumObjectBytes =
            long.TryParse(configuredMaximum, NumberStyles.Integer, CultureInfo.InvariantCulture, out long configured) &&
            configured > 0
                ? configured
                : FileManagementOptions.DefaultMaximumObjectBytes;

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maximumObjectBytes;
        });
    }

    private sealed class FileDownloadHttpResult(FileDownload download) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            FileStorageObjectProperties properties = download.File.Properties;
            httpContext.Response.ContentType = properties.ContentType;
            httpContext.Response.ContentLength = properties.ContentLength;
            httpContext.Response.Headers.CacheControl = "private, no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            httpContext.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            if (properties.FileName is not null)
            {
                ContentDispositionHeaderValue contentDisposition = new("attachment");
                contentDisposition.SetHttpFileName(properties.FileName);
                httpContext.Response.Headers.ContentDisposition = contentDisposition.ToString();
            }

            await download.File.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
    }
}
