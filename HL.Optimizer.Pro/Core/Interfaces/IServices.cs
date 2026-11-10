using HL.Optimizer.Core.Models;
namespace HL.Optimizer.Core.Interfaces;
public interface ISystemProbe
{
    Task<IReadOnlyList<Metric>> ReadAsync(CancellationToken token);
    Task<IReadOnlyList<InventoryRow>> InventoryAsync(string category, CancellationToken token);
}
public interface IOperationJournal
{
    Task AppendAsync(OperationRecord record, CancellationToken token = default);
    Task<IReadOnlyList<OperationRecord>> ReadAsync(CancellationToken token = default);
}
public interface IReversibleSetting
{
    TaskDefinition Definition { get; }
    string Read();
    void Write(string value);
    string Target { get; }
}
