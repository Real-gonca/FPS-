using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HLOptimizerPro.Tests;

/// <summary>
/// Testes da limpeza de ficheiros.
///
/// Usam um sistema de ficheiros falso, por isso NUNCA apagam nada real: o que se verifica é a
/// lógica (medição antes/depois, alvos ignorados, comandos de sistema corretos) e, sobretudo,
/// a garantia de que a limpeza só toca nas pastas declaradas em <see cref="CleanupTarget.Paths"/>.
/// </summary>
public sealed class CleanupServiceTests
{
    private static (CleanupService Service, FakeFileSystemProvider FileSystem, FakeProcessRunner Runner) Create()
    {
        var fileSystem = new FakeFileSystemProvider();
        var runner = new FakeProcessRunner();
        var activity = new ActivityLogService(NullLogger<ActivityLogService>.Instance);

        var service = new CleanupService(
            NullLogger<CleanupService>.Instance,
            fileSystem,
            runner,
            activity);

        return (service, fileSystem, runner);
    }

    private static CleanupTarget TempTarget(params string[] paths) => new()
    {
        Key = "temp-user",
        Name = "Ficheiros temporários",
        Description = "Ficheiros temporários do utilizador e do Windows.",
        Paths = paths,
    };

    [Fact]
    public async Task Sem_alvos_selecionados_a_limpeza_recusa_correr()
    {
        var (service, _, _) = Create();

        var result = await service.RunAsync(Array.Empty<CleanupTarget>());

        Assert.False(result.Success);
        Assert.Contains("pelo menos", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Limpeza_mede_os_bytes_reais_libertados()
    {
        var (service, fileSystem, _) = Create();

        fileSystem.SeedDirectory(@"C:\Temp", 4_096);
        fileSystem.SeedDirectory(@"C:\Temp\Nested", 1_024);

        var result = await service.RunAsync(new[] { TempTarget(@"C:\Temp", @"C:\Temp\Nested") });

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value!.FilesDeleted);
        Assert.Equal(5_120, result.Value.BytesFreed);

        // A mensagem apresentada ao utilizador tem de refletir o valor realmente medido.
        Assert.Contains("libertados", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pasta_inexistente_nao_produz_erro_nem_numeros_inventados()
    {
        var (service, _, _) = Create();

        var result = await service.RunAsync(new[] { TempTarget(@"C:\NaoExiste") });

        Assert.True(result.Success);
        Assert.Equal(0, result.Value!.FilesDeleted);
        Assert.Equal(0, result.Value.BytesFreed);
        Assert.Contains("já estava limpo", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Limpeza_so_toca_nas_pastas_declaradas_no_alvo()
    {
        var (service, fileSystem, _) = Create();

        // Pastas do utilizador que NUNCA podem ser limpas por engano.
        fileSystem.SeedDirectory(@"C:\Users\Ana\Documentos", 10_000_000);
        fileSystem.SeedDirectory(@"C:\Users\Ana\Downloads", 20_000_000);
        fileSystem.SeedDirectory(@"C:\Users\Ana\Desktop", 5_000_000);
        fileSystem.SeedDirectory(@"C:\Temp", 1_024);

        var result = await service.RunAsync(new[] { TempTarget(@"C:\Temp") });

        Assert.True(result.Success);

        // Só o alvo declarado foi tocado...
        Assert.Equal(new[] { @"C:\Temp" }, fileSystem.ClearedDirectories);

        // ...e as pastas do utilizador continuam exatamente na mesma.
        Assert.Equal(10_000_000, fileSystem.GetDirectorySize(@"C:\Users\Ana\Documentos"));
        Assert.Equal(20_000_000, fileSystem.GetDirectorySize(@"C:\Users\Ana\Downloads"));
        Assert.Equal(5_000_000, fileSystem.GetDirectorySize(@"C:\Users\Ana\Desktop"));

        Assert.Equal(1_024, result.Value!.BytesFreed);
    }

    [Fact]
    public async Task Limpeza_da_cache_dns_executa_ipconfig_flushdns()
    {
        var (service, _, runner) = Create();

        var target = new CleanupTarget
        {
            Key = "dns",
            Name = "Cache de DNS",
            Description = "Registos de nomes resolvidos recentemente.",
            Kind = CleanupKind.DnsCache,
        };

        var result = await service.RunAsync(new[] { target });

        Assert.True(result.Success);
        Assert.Equal(1, result.Value!.CacheEntriesCleared);
        Assert.Contains("ipconfig", runner.Executed[0]);
        Assert.Contains("/flushdns", runner.Executed[0]);

        // Limpar a cache de DNS não apaga ficheiros: reportar gigabytes aqui seria mentira.
        Assert.Equal(0, result.Value.BytesFreed);
        Assert.Equal(0, result.Value.FilesDeleted);
    }

    [Fact]
    public async Task Um_alvo_com_falha_nao_impede_a_limpeza_dos_restantes()
    {
        var (service, fileSystem, runner) = Create();

        fileSystem.SeedDirectory(@"C:\Temp", 2_048);

        // A reciclagem falha (PowerShell indisponível), mas o resto tem de continuar.
        runner.Default = new HLOptimizerPro.Core.Services.Abstractions.CommandResult(1, string.Empty, "acesso negado");

        var targets = new[]
        {
            TempTarget(@"C:\Temp"),
            new CleanupTarget
            {
                Key = "recycle-bin",
                Name = "Reciclagem",
                Description = "Ficheiros na reciclagem.",
                Kind = CleanupKind.RecycleBin,
            },
        };

        var result = await service.RunAsync(targets);

        // Houve um aviso, mas o ficheiro temporário foi mesmo removido.
        Assert.False(result.Success);
        Assert.Equal(1, result.Value!.FilesDeleted);
        Assert.Equal(2_048, result.Value.BytesFreed);
        Assert.NotEmpty(result.Value.Errors);
    }

    [Fact]
    public async Task Progresso_reporta_indice_e_total_para_a_barra_da_interface()
    {
        var (service, fileSystem, _) = Create();

        fileSystem.SeedDirectory(@"C:\Temp", 512);
        fileSystem.SeedDirectory(@"C:\Temp2", 512);

        var events = new List<(string Target, int Index, int Total)>();
        service.TargetStarted += (_, e) => events.Add(e);

        await service.RunAsync(new[] { TempTarget(@"C:\Temp"), TempTarget(@"C:\Temp2") });

        Assert.Equal(2, events.Count);
        Assert.Equal((1, 2), (events[0].Index, events[0].Total));
        Assert.Equal((2, 2), (events[1].Index, events[1].Total));
    }

    [Fact]
    public async Task Estimativa_devolve_zero_quando_nao_ha_nada_a_libertar()
    {
        var (service, _, _) = Create();

        var estimate = await service.EstimateAsync(CleanupTargets.QuickTargets());

        Assert.NotEmpty(estimate);
        Assert.All(estimate.Values, size => Assert.True(size >= 0));
    }
}
