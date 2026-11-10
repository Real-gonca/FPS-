using System.Text.Json;
using HL.Optimizer.Core.Interfaces;
using HL.Optimizer.Core.Models;
namespace HL.Optimizer.Core.Services;
public sealed class JsonJournal(string directory) : IOperationJournal
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task AppendAsync(OperationRecord record, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{DateTime.UtcNow.Ticks}-{Guid.NewGuid():N}.json");
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(record), token);
            File.Move(temp, path); // separate atomic records; never truncate a previous backup
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<OperationRecord>> ReadAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var list = new List<OperationRecord>();
            if (!Directory.Exists(directory)) return list;
            foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                list.Add(JsonSerializer.Deserialize<OperationRecord>(await File.ReadAllTextAsync(file, token)) ?? throw new InvalidDataException(file));
            return list;
        }
        finally { gate.Release(); }
    }
}
