using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static RefreshToken ToRefreshToken(this Dictionary<string, AttributeValue> row) =>
        new(
            row["RefreshTokenSecretHash"].S,
            Guid.Parse(row["UserId"].S),
            row["Email"].S,
            DateTimeOffset.FromUnixTimeSeconds(
                long.Parse(row["ExpiresAt"].N, CultureInfo.InvariantCulture)
            ),
            DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            row.TryGetValue("RevokedAt", out var revokedAt)
                ? DateTimeOffset.Parse(revokedAt.S, CultureInfo.InvariantCulture)
                : null
        );
}
