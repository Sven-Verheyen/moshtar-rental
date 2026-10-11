namespace Moshtar.Application.Carts;

/// <summary>Het pad van de link in de herinneringsmail, die de winkelkar opent, ook op een ander toestel.</summary>
public static class CartReminderPath
{
    public const string Route = "/winkelkar/herinnering/{key}";

    public static string For(string reminderKey) => "/winkelkar/herinnering/" + Uri.EscapeDataString(reminderKey);
}
