using System.Net;
using System.Text;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HLOptimizerPro.Tests;

/// <summary>
/// Testes do benchmark de DNS.
///
/// O benchmark fala UDP a sério, por isso estes testes verificam as peças que conseguem ser
/// verificadas sem rede: a construção do pacote DNS (RFC 1035), a validação de respostas e o
/// cálculo de latência/jitter/pontuação. Um teste final confirma o comportamento degradado
/// (servidor inexistente → resultado sem resposta, nunca números inventados).
/// </summary>
public sealed class DnsBenchmarkServiceTests
{
    private static DnsProvider Provider(string primary = "1.1.1.1", string key = "cloudflare") => new()
    {
        Key = key,
        Name = "Cloudflare",
        Primary = primary,
        Secondary = "1.0.0.1",
        Country = "Global",
        Notes = "Rápido e com forte compromisso de privacidade.",
    };

    // =====================================================================
    //  Construção do pacote DNS
    // =====================================================================

    [Fact]
    public void BuildQuery_respeita_o_formato_RFC_1035()
    {
        const ushort id = 0xABCD;
        var query = DnsBenchmarkService.BuildQuery(id, "exemplo.pt");

        // Cabeçalho: ID, flags (consulta recursiva), contagens.
        Assert.Equal(0xAB, query[0]);
        Assert.Equal(0xCD, query[1]);
        Assert.Equal(0x01, query[2]);          // Flags high byte
        Assert.Equal(0x00, query[3]);          // Flags low byte → 0x0100 = RD
        Assert.Equal(0x00, query[4]);          // QDCOUNT high
        Assert.Equal(0x01, query[5]);          // QDCOUNT low = 1 pergunta

        // Nome codificado como etiquetas de tamanho + conteúdo.
        Assert.Equal(7, query[12]);
        Assert.Equal("exemplo", Encoding.ASCII.GetString(query, 13, 7));
        Assert.Equal(2, query[20]);
        Assert.Equal("pt", Encoding.ASCII.GetString(query, 21, 2));
        Assert.Equal(0, query[23]);            // fim do nome

        // QTYPE = A (1) e QCLASS = IN (1).
        Assert.Equal(0x00, query[24]);
        Assert.Equal(0x01, query[25]);
        Assert.Equal(0x00, query[26]);
        Assert.Equal(0x01, query[27]);

        // 12 (cabeçalho) + 8 ("exemplo") + 3 ("pt") + 1 (raiz) + 4 (tipo/classe) = 28 bytes.
        Assert.Equal(28, query.Length);
    }

    [Fact]
    public void BuildQuery_dois_pedidos_diferentes_geram_pacotes_diferentes()
    {
        var first = DnsBenchmarkService.BuildQuery(1, "hlopt.pt");
        var second = DnsBenchmarkService.BuildQuery(2, "hlopt.pt");

        Assert.NotEqual(first, second);
    }

    // =====================================================================
    //  Validação das respostas
    // =====================================================================

    private static byte[] Response(ushort id, bool isResponse = true, int length = 64)
    {
        var buffer = new byte[length];
        buffer[0] = (byte)(id >> 8);
        buffer[1] = (byte)(id & 0xFF);
        buffer[2] = isResponse ? (byte)0x81 : (byte)0x01;   // QR (+ RD/RA)
        buffer[3] = 0x80;                                    // RCODE 0
        return buffer;
    }

    [Fact]
    public void IsValidResponse_aceita_resposta_com_o_mesmo_id()
        => Assert.True(DnsBenchmarkService.IsValidResponse(Response(0x1234), 0x1234));

    [Fact]
    public void IsValidResponse_rejeita_id_desconhecido()
        => Assert.False(DnsBenchmarkService.IsValidResponse(Response(0x1234), 0x1235));

    [Fact]
    public void IsValidResponse_rejeita_um_pacote_que_nao_e_resposta()
        => Assert.False(DnsBenchmarkService.IsValidResponse(Response(7, isResponse: false), 7));

    [Fact]
    public void IsValidResponse_rejeita_pacotes_truncados_ou_vazios()
    {
        Assert.False(DnsBenchmarkService.IsValidResponse(ReadOnlySpan<byte>.Empty, 1));
        Assert.False(DnsBenchmarkService.IsValidResponse(new byte[] { 0, 1, 0x81 }, 1));
    }

    // =====================================================================
    //  Cálculo da pontuação (latência + jitter + fiabilidade)
    // =====================================================================

    [Fact]
    public void Servidor_rapido_e_estavel_recebe_pontuacao_maxima()
    {
        // Amostras iguais (sem jitter) e abaixo de 10 ms: o cenário perfeito.
        var result = DnsBenchmarkService.BuildResult(Provider(), new List<double> { 9, 9, 9 }, sent: 3, failures: 0);

        Assert.True(result.IsReachable);
        Assert.Equal(100, result.Score);
        Assert.Equal(9, result.LatencyMs);            // mostra-se a melhor amostra
        Assert.Equal(0, result.PacketLossPercent);
        Assert.Equal("Excelente", result.StatusText);
    }

    [Fact]
    public void Servidor_lento_perde_pontos_mas_continua_a_ser_medido()
    {
        var fast = DnsBenchmarkService.BuildResult(Provider(), new List<double> { 12, 13, 12 }, sent: 3, failures: 0);
        var slow = DnsBenchmarkService.BuildResult(Provider(), new List<double> { 90, 95, 92 }, sent: 3, failures: 0);

        Assert.True(slow.Score < fast.Score);
        Assert.True(slow.Score > 0);                   // 90 ms é lento, não é inutilizável
        Assert.Equal(90, slow.LatencyMs);
        Assert.Equal(5, slow.JitterMs);
    }

    [Fact]
    public void Instabilidade_aumenta_o_jitter_e_baixa_a_pontuacao()
    {
        var stable = DnsBenchmarkService.BuildResult(Provider(), new List<double> { 20, 20, 20 }, sent: 3, failures: 0);
        var unstable = DnsBenchmarkService.BuildResult(Provider(), new List<double> { 20, 55, 60 }, sent: 3, failures: 0);

        Assert.Equal(0, stable.JitterMs);
        Assert.Equal(40, unstable.JitterMs);
        Assert.True(unstable.Score < stable.Score, "O mesmo mínimo, com jitter alto, tem de pontuar pior.");
    }

    [Fact]
    public void Perda_de_pacotes_penaliza_a_fiabilidade()
    {
        var result = DnsBenchmarkService.BuildResult(
            Provider(), new List<double> { 15, 16, 15 }, sent: 5, failures: 2);

        Assert.Equal(3, result.QueriesSucceeded);
        Assert.Equal(5, result.QueriesSent);
        Assert.Equal(40, result.PacketLossPercent);
        Assert.True(result.Score < 80, "Com 40 % de perdas a pontuação tem de refletir o problema.");

        // A pontuação nunca pode ser negativa, por muitas perdas que existam.
        Assert.InRange(result.Score, 0, 100);
    }

    [Fact]
    public void Servidor_sem_qualquer_resposta_nao_inventa_latencia()
    {
        var result = DnsBenchmarkService.BuildResult(Provider("203.0.113.1"), new List<double>(), sent: 5, failures: 5);

        Assert.False(result.IsReachable);
        Assert.Equal(0, result.Score);
        Assert.Equal("Sem resposta", result.LatencyText);
        Assert.Equal("—", result.ScoreText);
        Assert.Equal("Inacessível", result.StatusText);
        Assert.Equal(100, result.PacketLossPercent);
    }

    // =====================================================================
    //  Catálogo de fornecedores
    // =====================================================================

    [Fact]
    public void Catalogo_inclui_os_fornecedores_anunciados_na_interface()
    {
        var keys = DnsBenchmarkService.Providers.Select(p => p.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains("google", keys);
        Assert.Contains("cloudflare", keys);
        Assert.Contains("opendns", keys);
        Assert.Contains("norton", keys);
        Assert.Contains("quad9", keys);
    }

    [Fact]
    public void Todos_os_servidores_do_catalogo_sao_enderecos_validos()
    {
        foreach (var provider in DnsBenchmarkService.Providers)
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.Name));
            Assert.NotEmpty(provider.Servers);

            foreach (var server in provider.Servers)
                Assert.True(IPAddress.TryParse(server, out var address) &&
                            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork,
                    $"{provider.Name}: «{server}» não é um IPv4 válido.");

            // O texto mostrado na UI tem de listar os endereços efetivamente usados.
            foreach (var server in provider.Servers)
                Assert.Contains(server, provider.ServersText);
        }
    }

    // =====================================================================
    //  Comportamento degradado (servidor que não existe)
    // =====================================================================

    [Fact]
    public async Task Servidor_inexistente_devolve_sem_resposta_sem_lancar_excecao()
    {
        var service = new DnsBenchmarkService(NullLogger<DnsBenchmarkService>.Instance)
        {
            QueryCount = 1,
            Timeout = TimeSpan.FromMilliseconds(150),
        };

        // 203.0.113.0/24 é a gama TEST-NET-3 (RFC 5737): nunca é encaminhada para fora.
        var result = await service.TestAsync(Provider("203.0.113.7", "inexistente"));

        Assert.False(result.IsReachable);
        Assert.Equal(0, result.Score);
        Assert.Equal(0, result.LatencyMs);
        Assert.Equal(1, result.QueriesSent);
        Assert.Equal(0, result.QueriesSucceeded);
    }
}
