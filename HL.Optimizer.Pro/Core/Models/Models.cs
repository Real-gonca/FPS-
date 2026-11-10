namespace HL.Optimizer.Core.Models;
public sealed record Metric(string Name, string Value, string Detail, double? Percent = null);
public sealed record Finding(string Name, string Result, string Recommendation, string Priority);
public sealed record InventoryRow(string Name, string Value, string Detail);
public sealed record CleanCandidate(string Path, long Bytes, DateTime LastWriteUtc, string Category);
public sealed record CleanScan(IReadOnlyList<CleanCandidate> Files, IReadOnlyList<string> Skipped);
public sealed record CleanResult(long Bytes, int Deleted, IReadOnlyList<string> Skipped);
public sealed record OperationRecord(Guid Id, DateTimeOffset Time, string TaskId, string Status, string Detail, string? Before = null);
public enum RiskLevel { Low, Moderate, High }
public sealed record TaskDefinition(string Id, string Title, string Description, string Category, RiskLevel Risk, string Compatibility, string ExpectedBenefit, bool Reversible);
