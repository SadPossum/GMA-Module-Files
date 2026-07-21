namespace Gma.Modules.Files.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Gma.Framework.Api.Modules;
using Gma.Framework.Cqrs.Infrastructure;
using Gma.Framework.FileManagement;
using Gma.Framework.FileManagement.LocalStorage;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Scoping.Infrastructure;
using Gma.Framework.Security;
using Gma.Modules.Files.Api;
using Gma.Modules.Files.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

[Trait("Category", "Integration")]
public sealed class FilesModuleTests
{
    [Fact]
    public void Default_profile_declares_private_objects_and_required_infrastructure()
    {
        Assert.Equal(FilesModuleMetadata.Name, FilesProfiles.Default.ModuleName);
        Assert.Contains(
            FilesProfiles.Default.Provides,
            feature => feature.Id == FilesCompositionFeatures.Objects);
        Assert.Equal(2, FilesProfiles.Default.Requires.Count);
    }

    [Fact]
    public async Task Api_enforces_subject_and_scope_and_returns_safe_download_headers()
    {
        await using RunningApplication running = await RunningApplication.StartAsync();
        Assert.Equal(
            1_048_576,
            running.Application.Services.GetRequiredService<IOptions<FormOptions>>().Value.MultipartBodyLengthLimit);

        using HttpClient anonymous = running.CreateClient(requestScope: "scope-a");
        using HttpResponseMessage anonymousResponse = await anonymous.GetAsync(
            "/api/files/019f4a7c-1000-7000-8000-000000000010");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using HttpClient owner = running.CreateClient("user-a", "scope-a", "scope-a");
        FileUploadResponse upload = await UploadTextAsync(owner, "private-content");

        using HttpResponseMessage ownerDownload = await owner.GetAsync(upload.DownloadPath);
        Assert.Equal(HttpStatusCode.OK, ownerDownload.StatusCode);
        Assert.Equal("private-content", await ownerDownload.Content.ReadAsStringAsync());
        Assert.True(ownerDownload.Headers.CacheControl?.NoStore);
        Assert.True(ownerDownload.Headers.CacheControl?.Private);
        Assert.Contains(ownerDownload.Headers.Pragma, directive => directive.Name == "no-cache");
        Assert.Equal("nosniff", Assert.Single(ownerDownload.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("attachment", ownerDownload.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("api.txt", ownerDownload.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);

        using HttpClient otherUser = running.CreateClient("user-b", "scope-a", "scope-a");
        using HttpResponseMessage otherUserDownload = await otherUser.GetAsync(upload.DownloadPath);
        using HttpResponseMessage otherUserDelete = await otherUser.DeleteAsync(upload.DownloadPath);
        Assert.Equal(HttpStatusCode.NotFound, otherUserDownload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherUserDelete.StatusCode);

        using HttpClient scopeMismatch = running.CreateClient("user-a", "scope-b", "scope-a");
        using HttpResponseMessage mismatchDownload = await scopeMismatch.GetAsync(upload.DownloadPath);
        Assert.Equal(HttpStatusCode.Forbidden, mismatchDownload.StatusCode);

        using HttpResponseMessage ownerDelete = await owner.DeleteAsync(upload.DownloadPath);
        using HttpResponseMessage afterDelete = await owner.GetAsync(upload.DownloadPath);
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Api_maps_disallowed_content_type_without_storing_the_upload()
    {
        await using RunningApplication running = await RunningApplication.StartAsync();
        using HttpClient owner = running.CreateClient("user-a", "scope-a", "scope-a");
        using MultipartFormDataContent form = new();
        using ByteArrayContent file = new(Encoding.UTF8.GetBytes("not-allowed"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", "payload.json");

        using HttpResponseMessage response = await owner.PostAsync("/api/files", form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.False(
            Directory.Exists(running.StorageRoot) &&
            Directory.EnumerateFiles(running.StorageRoot, "*", SearchOption.AllDirectories).Any());
    }

    [Theory]
    [InlineData(FileContentTypeDetectionStatus.Unrecognized, HttpStatusCode.UnsupportedMediaType)]
    [InlineData(FileContentTypeDetectionStatus.Unavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Api_maps_trusted_detection_failures_without_storing_the_upload(
        FileContentTypeDetectionStatus status,
        HttpStatusCode expectedStatus)
    {
        await using RunningApplication running = await RunningApplication.StartAsync(status);
        using HttpClient owner = running.CreateClient("user-a", "scope-a", "scope-a");
        using MultipartFormDataContent form = new();
        using ByteArrayContent file = new(Encoding.UTF8.GetBytes("untrusted"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "upload.txt");

        using HttpResponseMessage response = await owner.PostAsync("/api/files", form);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.False(
            Directory.Exists(running.StorageRoot) &&
            Directory.EnumerateFiles(running.StorageRoot, "*", SearchOption.AllDirectories).Any());
    }

    private static async Task<FileUploadResponse> UploadTextAsync(HttpClient client, string value)
    {
        using MultipartFormDataContent form = new();
        using ByteArrayContent file = new(Encoding.UTF8.GetBytes(value));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "api.txt");

        using HttpResponseMessage response = await client.PostAsync("/api/files", form);
        response.EnsureSuccessStatusCode();
        return Assert.IsType<FileUploadResponse>(await response.Content.ReadFromJsonAsync<FileUploadResponse>());
    }

    private sealed class RunningApplication(WebApplication application, string storageRoot) : IAsyncDisposable
    {
        public WebApplication Application { get; } = application;
        public string StorageRoot { get; } = storageRoot;

        public static async Task<RunningApplication> StartAsync(
            FileContentTypeDetectionStatus? detectionStatus = null)
        {
            string storageRoot = Path.Combine(Path.GetTempPath(), $"gma-files-api-{Guid.NewGuid():N}");
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Test"
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationIdentity:Namespace"] = "files-api-tests",
                ["Scoping:Enabled"] = "true",
                ["Scoping:HeaderName"] = "X-Scope-Id",
                ["Scoping:LocalDefaultScopeId"] = "default",
                ["FileManagement:Enabled"] = "true",
                ["FileManagement:Provider"] = "LocalStorage",
                ["FileManagement:MaximumObjectBytes"] = "1048576",
                ["FileManagement:AllowedContentTypes:0"] = "text/plain",
                ["FileManagement:LocalStorage:RootPath"] = storageRoot,
                ["Files:Uploads:RequireTrustedContentType"] = detectionStatus.HasValue.ToString()
            });

            if (detectionStatus is { } status)
            {
                ApiContentTypeDetector detector = new(status);
                builder.Services.AddSingleton<IFileContentTypeDetector>(detector);
                builder.Services.AddSingleton<IFileContentTypeDetectorReadiness>(detector);
            }

            builder.AddScopingInfrastructure();
            builder.AddCqrsInfrastructure();
            builder.AddLocalFileStorage();
            builder.Services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            builder.Services.AddAuthorization();
            builder.AddModule<FilesModule>();
            builder.ValidateModuleComposition();

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapModules();
            app.Urls.Add("http://127.0.0.1:0");
            await app.StartAsync();

            return new RunningApplication(app, storageRoot);
        }

        public HttpClient CreateClient(
            string? userId = null,
            string? tokenScope = null,
            string? requestScope = null)
        {
            IServer server = this.Application.Services.GetRequiredService<IServer>();
            string address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
            HttpClient client = new() { BaseAddress = new Uri(address) };
            if (userId is not null)
            {
                client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, userId);
            }

            if (tokenScope is not null)
            {
                client.DefaultRequestHeaders.Add(TestAuthenticationHandler.ScopeHeader, tokenScope);
            }

            if (requestScope is not null)
            {
                client.DefaultRequestHeaders.Add("X-Scope-Id", requestScope);
            }

            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await this.Application.DisposeAsync();
            if (Directory.Exists(this.StorageRoot))
            {
                Directory.Delete(this.StorageRoot, recursive: true);
            }
        }
    }

    private sealed class ApiContentTypeDetector(FileContentTypeDetectionStatus status) :
        IFileContentTypeDetector,
        IFileContentTypeDetectorReadiness
    {
        public ValueTask<FileContentTypeDetectionResult> DetectAsync(
            FileContentTypeDetectionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(status switch
            {
                FileContentTypeDetectionStatus.Detected =>
                    FileContentTypeDetectionResult.Detected("api-test-detector", "text/plain"),
                FileContentTypeDetectionStatus.Unrecognized =>
                    FileContentTypeDetectionResult.Unrecognized("api-test-detector"),
                _ => FileContentTypeDetectionResult.Unavailable("api-test-detector")
            });
        }

        public ValueTask<FileContentCapabilityReadiness> CheckReadinessAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(FileContentCapabilityReadiness.Ready("api-test-detector"));
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "FilesTests";
        public const string UserHeader = "X-Test-User";
        public const string ScopeHeader = "X-Test-Token-Scope";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!this.Request.Headers.TryGetValue(UserHeader, out var userValues) ||
                string.IsNullOrWhiteSpace(userValues.ToString()))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            List<Claim> claims = [new(ClaimTypes.NameIdentifier, userValues.ToString())];
            if (this.Request.Headers.TryGetValue(ScopeHeader, out var scopeValues) &&
                !string.IsNullOrWhiteSpace(scopeValues.ToString()))
            {
                claims.Add(new Claim(ApplicationClaimNames.ScopeId, scopeValues.ToString()));
            }

            ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName));
            AuthenticationTicket ticket = new(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
