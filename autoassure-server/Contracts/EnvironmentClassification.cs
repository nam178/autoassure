namespace A2.Server.Contracts;

/// <summary>Whether an Environment is a live Production system or a non-production one (staging, dev, ...).</summary>
public enum EnvironmentClassification
{
    Production,
    NonProduction,
}
