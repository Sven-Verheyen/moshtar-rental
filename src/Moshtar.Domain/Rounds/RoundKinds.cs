namespace Moshtar.Domain.Rounds;

/// <summary>Hoe een soort ronde heet in het back office en in een adres (?ronde=levering).</summary>
public static class RoundKinds
{
    /// <summary>"Leverronde" of "Ophaalronde".</summary>
    public static string Name(this RoundKind kind) => kind == RoundKind.Collection ? "Ophaalronde" : "Leverronde";

    /// <summary>"levering" of "ophaling", voor in een adres of een data-attribuut.</summary>
    public static string Slug(this RoundKind kind) => kind == RoundKind.Collection ? "ophaling" : "levering";

    /// <summary>De ronde van een <see cref="Slug"/>; al de rest is de leverronde.</summary>
    public static RoundKind FromSlug(string? slug) => slug == "ophaling" ? RoundKind.Collection : RoundKind.Delivery;
}
