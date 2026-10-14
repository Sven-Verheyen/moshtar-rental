using Moshtar.Application.Catalog;
using Moshtar.Application.Images;
using Moshtar.Domain.Catalog;

namespace Moshtar.Infrastructure.Catalog;

/// <summary>
/// De foto's van een artikel of pakket: een geordende lijst van sleutels in de afbeeldingenopslag, tot
/// <see cref="Photo.Max"/>. Uploaden gebeurt vóór de wijziging in de database (verkleinen duurt even); lukt die
/// wijziging niet, dan ruimt <see cref="ForgetAsync"/> de nieuwe afbeeldingen weer op.
/// </summary>
internal sealed class PhotoAlbum(IImageStore images)
{
    /// <summary>
    /// Uploadt de foto's, alles of niets, als er naast <paramref name="present"/> foto's plaats voor is. Gooit een
    /// <see cref="ImageRejectedException"/> die zegt welk bestand niet kan, of dat het er te veel zijn.
    /// </summary>
    public async Task<IReadOnlyList<string>> UploadAsync(int present, IReadOnlyList<PhotoUpload> uploads, CancellationToken ct)
    {
        CheckRoom(present, uploads.Count);
        var keys = new List<string>();
        try
        {
            foreach (var upload in uploads)
            {
                try
                {
                    keys.Add(await images.UploadAsync(upload.Content, ct));
                }
                catch (ImageRejectedException e)
                {
                    throw new ImageRejectedException($"{upload.FileName}: {e.Message} Er werd geen enkele foto toegevoegd.");
                }
            }
        }
        catch
        {
            await ForgetAsync(keys, CancellationToken.None);
            throw;
        }
        return keys;
    }

    /// <summary>Gooit een <see cref="ImageRejectedException"/> als er naast <paramref name="present"/> foto's geen <paramref name="adding"/> meer bij kunnen.</summary>
    public static void CheckRoom(int present, int adding)
    {
        if (present + adding <= Photo.Max) return;
        var room = Math.Max(0, Photo.Max - present);
        throw new ImageRejectedException(room == 0
            ? $"Er kunnen maximaal {Photo.Max} foto's bij; verwijder er eerst een."
            : $"Er kunnen maximaal {Photo.Max} foto's bij: je kan er nog {room} toevoegen. Er werd geen enkele foto toegevoegd.");
    }

    /// <summary>Zet nieuwe foto's achteraan.</summary>
    public static void Append(List<Photo> photos, IEnumerable<string> keys)
    {
        var next = photos.Count == 0 ? 1 : photos.Max(p => p.SortOrder) + 1;
        foreach (var key in keys) photos.Add(new Photo { Key = key, SortOrder = next++ });
    }

    /// <summary>De sleutels in hun volgorde, de hoofdfoto eerst.</summary>
    public static IReadOnlyList<string> Keys(IEnumerable<Photo> photos) => photos.OrderBy(p => p.SortOrder).Select(p => p.Key).ToList();

    /// <summary>Zet de foto's in deze volgorde; onwaar (en niets gewijzigd) als de lijst niet elke foto precies één keer bevat.</summary>
    public static bool Reorder(List<Photo> photos, IReadOnlyList<string> keys)
    {
        if (keys.Count != photos.Count || keys.Distinct().Count() != photos.Count || !photos.All(p => keys.Contains(p.Key)))
            return false;
        var order = keys.ToList();
        foreach (var photo in photos) photo.SortOrder = order.IndexOf(photo.Key) + 1;
        return true;
    }

    /// <summary>Haalt een foto uit de lijst; onwaar als ze er niet (meer) in zit.</summary>
    public static bool Remove(List<Photo> photos, string key)
    {
        if (photos.FirstOrDefault(p => p.Key == key) is not { } photo) return false;
        photos.Remove(photo);
        var order = 1;
        foreach (var remaining in photos.OrderBy(p => p.SortOrder)) remaining.SortOrder = order++;
        return true;
    }

    /// <summary>Verwijdert deze afbeeldingen uit de opslag, nadat niets er nog naar verwijst.</summary>
    public async Task ForgetAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        foreach (var key in keys) await images.DeleteAsync(key, ct);
    }
}
