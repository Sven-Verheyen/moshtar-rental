using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Moshtar.Application.Tenancy;

namespace Moshtar.Infrastructure.Images;

/// <summary>De container in Azure Blob Storage, één voor het hele platform; de sleutels scheiden de verhuurders.</summary>
internal sealed class ImageContainer(IOptions<ImageOptions> options)
{
    private int _created;

    public BlobContainerClient Client { get; } =
        new(options.Value.AzureBlobStorageConnectionString, options.Value.Container);

    /// <summary>Maakt de container aan, met publieke leestoegang tot de blobs, als dat nog niet gebeurde.</summary>
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _created) == 1) return;
        await Client.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);
        Volatile.Write(ref _created, 1);
    }
}

/// <summary>Afbeeldingen in Azure Blob Storage, publiek leesbaar via de container of een CDN ervoor.</summary>
internal sealed class AzureBlobImageStore(ImageContainer container, IOptions<ImageOptions> options, ITenantContext tenantContext)
    : ImageStore(tenantContext)
{
    protected override string PublicBaseUrl =>
        string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl) ? container.Client.Uri.AbsoluteUri : options.Value.PublicBaseUrl;

    protected override async Task SaveAsync(string path, Stream content, CancellationToken ct)
    {
        await container.EnsureCreatedAsync(ct);
        // Een nieuw pad krijgt nooit een bestaande blob: de sleutel is nieuw. Overschrijven kan dus niet.
        await container.Client.GetBlobClient(path).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "image/webp", CacheControl = "public, max-age=31536000, immutable" },
            Conditions = new BlobRequestConditions { IfNoneMatch = Azure.ETag.All },
        }, ct);
    }

    protected override Task DeleteFileAsync(string path, CancellationToken ct) =>
        container.Client.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct);
}
