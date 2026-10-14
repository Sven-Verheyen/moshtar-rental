using Microsoft.Extensions.Options;
using Moshtar.Application.Tenancy;

namespace Moshtar.Infrastructure.Images;

/// <summary>De map van de lokale afbeeldingenopslag; de webapp serveert ze onder <see cref="RequestPath"/>.</summary>
public sealed record LocalImageFolder(string Path)
{
    public const string RequestPath = "/afbeeldingen";
}

/// <summary>
/// Afbeeldingen in een map van de app, zonder Azure (lokaal en in de tests). Het publieke adres ligt op het
/// hoofddomein van de verhuurder, of op <see cref="ImageOptions.PublicBaseUrl"/> als die ingesteld is (lokaal: localhost).
/// </summary>
internal sealed class LocalImageStore(LocalImageFolder folder, IOptions<ImageOptions> options, ITenantContext tenantContext)
    : ImageStore(tenantContext)
{
    protected override string PublicBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl)) return options.Value.PublicBaseUrl;
            var tenant = TenantContext.Tenant;
            var host = tenant?.PrimaryHost ?? tenant?.Hosts.FirstOrDefault()?.Hostname;
            return host is null ? LocalImageFolder.RequestPath : $"https://{host}{LocalImageFolder.RequestPath}";
        }
    }

    protected override async Task SaveAsync(string path, Stream content, CancellationToken ct)
    {
        var file = FullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        await using var output = new FileStream(file, FileMode.CreateNew);
        await content.CopyToAsync(output, ct);
    }

    protected override Task DeleteFileAsync(string path, CancellationToken ct)
    {
        File.Delete(FullPath(path));
        var directory = System.IO.Path.GetDirectoryName(FullPath(path))!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
        return Task.CompletedTask;
    }

    private string FullPath(string path) => System.IO.Path.Combine(folder.Path, path);
}
