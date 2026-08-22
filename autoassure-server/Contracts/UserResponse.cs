namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>The signed-in AutoAssure user, as returned to the client after authentication.</summary>
public record UserResponse(
    string Id,
    string FirstName,
    string LastName,
    string Email,
    bool EmailVerified
);
