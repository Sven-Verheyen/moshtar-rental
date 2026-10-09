using Moshtar.Domain.Common;

namespace Moshtar.Domain.Catalog;

public class Category : TenantEntity
{
    public string Slug { get; set; } = "";
    public int SortOrder { get; set; }
    public List<Translation> Translations { get; set; } = [];
}
