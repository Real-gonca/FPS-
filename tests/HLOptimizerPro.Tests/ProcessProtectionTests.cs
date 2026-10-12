using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using Xunit;

namespace HLOptimizerPro.Tests;

/// <summary>
/// Testes da lista de proteção de processos.
///
/// Esta é uma das promessas de segurança do produto: a aplicação NUNCA encerra processos do
/// sistema. Se a lista falhar, o utilizador pode perder o ambiente de trabalho ou apanhar um
/// ecrã azul — por isso aqui testamos explicitamente processos reais do Windows, e também o
/// princípio "em caso de dúvida, não mexer".
/// </summary>
public sealed class ProcessProtectionTests
{
    [Theory]
    [InlineData("System")]
    [InlineData("Idle")]
    [InlineData("smss.exe")]
    [InlineData("csrss")]
    [InlineData("winlogon")]
    [InlineData("services.exe")]
    [InlineData("lsass")]
    [InlineData("svchost")]
    [InlineData("dwm")]
    [InlineData("explorer")]                 // encerrar o Explorer faz perder trabalho do utilizador
    [InlineData("Registry")]
    [InlineData("Memory Compression")]
    [InlineData("SecurityHealthService")]
    [InlineData("MsMpEng")]                  // Microsoft Defender
    [InlineData("HLOptimizerPro")]           // a própria aplicação
    public void Processos_do_sistema_estao_protegidos(string processName)
        => Assert.True(ProcessProtectionService.IsProtected(processName),
            $"«{processName}» tem de estar na lista de proteção: encerrá-lo pode instabilizar o Windows.");

    [Theory]
    [InlineData("svchost#3")]                // serviços agrupados (padrão de nome)
    [InlineData("dllhost.exe")]
    [InlineData("taskhostw")]
    [InlineData("WinRshost")]                // serviços com prefixo Win + maiúscula
    public void Padroes_de_nomes_de_servicos_tambem_sao_protegidos(string processName)
        => Assert.True(ProcessProtectionService.IsProtected(processName));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Nome_desconhecido_e_tratado_como_protegido(string? processName)
        => Assert.True(ProcessProtectionService.IsProtected(processName),
            "Sem nome fiável não se encerra nada — nunca o contrário.");

    [Theory]
    [InlineData("discord")]
    [InlineData("chrome")]
    [InlineData("steam")]
    [InlineData("Spotify")]
    [InlineData("obs64")]
    public void Aplicacoes_do_utilizador_nao_estao_protegidas(string processName)
        => Assert.False(ProcessProtectionService.IsProtected(processName));

    [Theory]
    [InlineData("csrss", true)]
    [InlineData("winlogon", true)]
    [InlineData("services", true)]
    [InlineData("lsass.exe", true)]
    [InlineData("discord", false)]
    [InlineData("chrome.exe", false)]
    public void Processos_criticos_sao_identificados_como_tal(string processName, bool expected)
        => Assert.Equal(expected, ProcessProtectionService.IsSystemCritical(processName));

    [Fact]
    public void CanSafelyTerminate_recusa_sempre_processos_protegidos_ou_criticos()
    {
        var prohibited = new[]
        {
            new ProcessUsageInfo { Pid = 100, Name = "svchost", IsProtected = true, IsSystemCritical = true },
            new ProcessUsageInfo { Pid = 200, Name = "explorer", IsProtected = true, IsSystemCritical = true },
            new ProcessUsageInfo { Pid = 4, Name = "qualquer", IsProtected = false, IsSystemCritical = false },
            new ProcessUsageInfo { Pid = 2, Name = "System", IsProtected = true, IsSystemCritical = true },
        };

        Assert.All(prohibited, process => Assert.False(ProcessProtectionService.CanSafelyTerminate(process)));
    }

    [Fact]
    public void CanSafelyTerminate_permite_processos_normais_do_utilizador()
    {
        var process = new ProcessUsageInfo
        {
            Pid = 4321,
            Name = "Discord",
            WorkingSetBytes = 700L * 1024 * 1024,
            IsProtected = false,
            IsSystemCritical = false,
        };

        Assert.True(ProcessProtectionService.CanSafelyTerminate(process));
    }

    [Fact]
    public void Processos_do_utilizador_em_Windows_system32_nao_devem_ser_tratados_como_normais()
    {
        // A verificação de pasta é uma defesa extra usada pela Memória/Otimizações.
        var system32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "qualquer.dll");

        // Em sistemas sem pasta Windows definida (CI Linux), o serviço devolve false: aceitamos ambos,
        // porque o que importa é que nunca devolva "true" para um caminho vazio.
        Assert.False(ProcessProtectionService.IsInsideWindowsDirectory(null));
        Assert.False(ProcessProtectionService.IsInsideWindowsDirectory("   "));

        if (!string.IsNullOrWhiteSpace(Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
            Assert.True(ProcessProtectionService.IsInsideWindowsDirectory(system32));
    }
}
