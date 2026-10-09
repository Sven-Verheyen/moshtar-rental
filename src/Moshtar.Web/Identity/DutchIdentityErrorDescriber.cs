using Microsoft.AspNetCore.Identity;

namespace Moshtar.Web.Identity;

/// <summary>De foutboodschappen van Identity die een gebruiker te zien kan krijgen, in het Nederlands.</summary>
internal sealed class DutchIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Een wachtwoord moet minstens {length} tekens lang zijn." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Een wachtwoord moet minstens {uniqueChars} verschillende tekens bevatten." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = $"Er bestaat al een gebruiker met e-mailadres {email}." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = $"Er bestaat al een gebruiker met e-mailadres {userName}." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = $"'{email}' is geen geldig e-mailadres." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Deze link is verlopen of al gebruikt." };
}
