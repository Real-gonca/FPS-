using System.Collections;

namespace HLProOptimizer.Core.Common;

/// <summary>
/// Buffer circular de capacidade fixa e thread-safe.
/// Usado pelo monitoramento para manter os últimos N segundos de amostras
/// (padrão 60) sem pressão de alocação/GC - crítico para gráficos em tempo real.
/// </summary>
/// <typeparam name="T">Tipo armazenado.</typeparam>
public sealed class RingBuffer<T> : IEnumerable<T>
{
    private readonly T[] _buffer;
    private readonly object _sync = new();
    private int _head;
    private int _count;

    /// <summary>Cria um buffer com a capacidade informada.</summary>
    /// <param name="capacity">Número máximo de itens (&gt; 0).</param>
    /// <exception cref="ArgumentOutOfRangeException">Quando <paramref name="capacity"/> &lt;= 0.</exception>
    public RingBuffer(int capacity)
    {
        Guard.InRange(capacity, 1, int.MaxValue, nameof(capacity));
        _buffer = new T[capacity];
        Capacity = capacity;
    }

    /// <summary>Capacidade máxima do buffer.</summary>
    public int Capacity { get; }

    /// <summary>Quantidade de itens atualmente armazenados.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    /// <summary>Indica se o buffer já está cheio (descartará o item mais antigo).</summary>
    public bool IsFull => Count == Capacity;

    /// <summary>
    /// Adiciona um item. Quando cheio, sobrescreve o item mais antigo.
    /// </summary>
    /// <param name="item">Item a adicionar.</param>
    /// <returns>O item descartado, ou <c>default</c> quando havia espaço livre.</returns>
    public T? Add(T item)
    {
        lock (_sync)
        {
            T? evicted = default;

            if (_count == Capacity)
            {
                evicted = _buffer[_head];
                _buffer[_head] = item;
                _head = (_head + 1) % Capacity;
            }
            else
            {
                _buffer[(_head + _count) % Capacity] = item;
                _count++;
            }

            return evicted;
        }
    }

    /// <summary>
    /// Retorna uma cópia dos itens em ordem cronológica (mais antigo primeiro).
    /// </summary>
    public IReadOnlyList<T> ToList()
    {
        lock (_sync)
        {
            var result = new T[_count];
            for (var i = 0; i < _count; i++)
            {
                result[i] = _buffer[(_head + i) % Capacity];
            }

            return result;
        }
    }

    /// <summary>Remove todos os itens.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ToList().GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
