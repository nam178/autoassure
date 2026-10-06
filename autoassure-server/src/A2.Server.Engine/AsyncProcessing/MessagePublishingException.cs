namespace A2.Server.Engine.AsyncProcessing;

public sealed class MessagePublishingException(
    string message,
    Exception innerException
) : Exception(message, innerException);
