namespace A2.Server.Repositories;

/// <summary>Thrown when a DynamoDB row is missing an attribute that every writer always sets. Signals
/// stored data corruption rather than an expected absence -- callers MUST NOT catch this to fall back to
/// a default.</summary>
public class CorruptedDynamoDbRowException(string message) : Exception(message);
