using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(this User user) =>
        new()
        {
            ["Id"] = new(user.Id.ToString()),
            ["GoogleUserId"] = new(user.GoogleUserId),
            ["FirstName"] = new(user.FirstName),
            ["LastName"] = new(user.LastName),
            ["Email"] = new(user.Email),
            ["EmailVerified"] = new() { BOOL = user.EmailVerified },
        };

    public static User ToUser(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            GoogleUserId = row["GoogleUserId"].S,
            FirstName = row["FirstName"].S,
            LastName = row["LastName"].S,
            Email = row["Email"].S,
            EmailVerified = row["EmailVerified"].BOOL ?? false,
        };
}
