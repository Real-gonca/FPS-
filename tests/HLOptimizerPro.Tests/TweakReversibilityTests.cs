using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.System;
using HLOptimizerPro.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Xunit;

namespace HLOptimizerPro.Tests;

/// <summary>
/// Testes da regra de ouro do produto: TODA a otimização é reversível.
///
/// Verificamos, sobre um registo falso em memória, os três caminhos que o utilizador pode seguir:
///   • aplicar  → guarda o valor original e escreve o novo;
///   • reverter → repõe exatamente o valor original (ou remove o que criámos);
///   • simular  → não escreve absolutamente nada.
/// </summary>
public sealed class TweakReversibilityTests
{
    private const string TestHive = "HKCU";
    private const string TestSubKey = @"Software\HLOptimizerPro\Testes";
    private const string TestValue = "MenuShowDelay";

    private static (TweakFactory Factory, FakeRegistryProvider Registry, FakeProcessRunner Runner) Create()
    {
        var registry = new FakeRegistryProvider();
        var runner = new FakeProcessRunner();

        var factory = new TweakFactory(registry, runner, NullLogger<TweakFactory>.Instance);

        return (factory, registry, runner);
    }

    private static RegistryTweakSpec Spec(string key = "performance.menu-delay") => new()
    {
        Key = key,
        Title = "Reduzir o atraso dos menus",
        Description = "Retira o atraso artificial ao abrir menus do Windows.",
        Hive = TestHive,
        SubKey = TestSubKey,
        ValueName = TestValue,
        Kind = RegistryValueKind.DWord,
        OptimizedValue = 0,
        Risk = "Baixo — apenas uma preferência de interface",
    };

    // =====================================================================
    //  Aplicar
    // =====================================================================

    [Fact]
    public void Aplicar_escreve_o_valor_otimizado_e_guarda_o_original()
    {
        var (factory, registry, _) = Create();
        registry.Seed(TestHive, TestSubKey, TestValue, 400);

        var action = factory.Create(Spec(), simulationMode: false);

        Assert.Equal("400", action.CurrentValue);
        Assert.False(action.IsAlreadyApplied!());

        action.Apply!(action);

        // O registo ficou com o valor otimizado...
        Assert.Equal(0, Convert.ToInt32(registry.GetValue(TestHive, TestSubKey, TestValue)));

        // ...e o valor original ficou guardado para a reversão.
        Assert.Equal("true", action.Backup[BackupKeys.RegExisted]);
        Assert.Equal("400", action.Backup[BackupKeys.RegOriginal]);
        Assert.Equal(TestHive, action.Backup[BackupKeys.RegHive]);
        Assert.Equal(TestSubKey, action.Backup[BackupKeys.RegSubKey]);

        // O predicado de "já aplicado" lê o sistema em tempo real.
        Assert.True(action.IsAlreadyApplied!());
    }

    [Fact]
    public void Aplicar_exporta_o_backup_do_registo_antes_de_alterar()
    {
        var (factory, registry, _) = Create();
        registry.Seed(TestHive, TestSubKey, TestValue, 400);

        var action = factory.Create(Spec(), simulationMode: false);
        action.Apply!(action);

        // Tem de existir um .reg com o estado anterior: é a rede de segurança manual.
        Assert.Single(registry.Exports);
        Assert.Contains("performance.menu-delay.reg", registry.Exports[0]);
    }

    // =====================================================================
    //  Reverter
    // =====================================================================

    [Fact]
    public void Reverter_repone_exatamente_o_valor_original()
    {
        var (factory, registry, _) = Create();
        registry.Seed(TestHive, TestSubKey, TestValue, 400);

        var action = factory.Create(Spec(), simulationMode: false);
        action.Apply!(action);

        var revert = action.Revert!(action);

        Assert.True(revert.Success);
        Assert.Equal(400, Convert.ToInt32(registry.GetValue(TestHive, TestSubKey, TestValue)));
    }

    [Fact]
    public void Reverter_remove_o_valor_que_nao_existia_antes()
    {
        var (factory, registry, _) = Create();

        var action = factory.Create(Spec(), simulationMode: false);
        Assert.Equal("não definido", action.CurrentValue);

        action.Apply!(action);
        Assert.Equal(0, Convert.ToInt32(registry.GetValue(TestHive, TestSubKey, TestValue)));

        var revert = action.Revert!(action);

        Assert.True(revert.Success);
        Assert.Equal("false", action.Backup[BackupKeys.RegExisted]);
        Assert.Null(registry.GetValue(TestHive, TestSubKey, TestValue));
    }

    [Fact]
    public void Aplicar_e_reverter_varias_vezes_mantem_o_estado_coerente()
    {
        var (factory, registry, _) = Create();
        registry.Seed(TestHive, TestSubKey, TestValue, 250);

        var action = factory.Create(Spec(), simulationMode: false);

        action.Apply!(action);
        Assert.Equal(0, Convert.ToInt32(registry.GetValue(TestHive, TestSubKey, TestValue)));

        action.Revert!(action);
        Assert.Equal(250, Convert.ToInt32(registry.GetValue(TestHive, TestSubKey, TestValue)));

        action.Apply!(action);
        Assert.Equal(0, Convert.ToInt32(registry.GetValue(TestHive, TestSubKey, TestValue)));
    }

    [Fact]
    public void Reverter_sem_backup_falha_com_mensagem_clara()
    {
        var (factory, _, _) = Create();

        // Ação criada mas nunca aplicada: não existe backup nenhum para restaurar.
        var action = factory.Create(Spec(), simulationMode: false);

        var revert = action.Revert!(action);

        Assert.False(revert.Success);
        Assert.Contains("backup", revert.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    //  Modo Simular
    // =====================================================================

    [Fact]
    public void Modo_simular_nao_escreve_nada_no_sistema()
    {
        var (factory, registry, runner) = Create();
        registry.Seed(TestHive, TestSubKey, TestValue, 400);

        var action = factory.Create(Spec(), simulationMode: true);

        action.Apply!(action);   // o motor chama Apply também em simulação; tem de ser inofensivo

        Assert.Empty(registry.Writes);            // nada foi escrito
        Assert.Empty(registry.Exports);           // nada foi exportado
        Assert.Empty(runner.Executed);            // nenhum comando externo foi executado
        Assert.Equal(400, Convert.ToInt32(registry.GetValue(TestHive, TestSubKey, TestValue)));
        Assert.False(action.Backup.ContainsKey(BackupKeys.RegOriginal));
    }

    [Fact]
    public void Modo_simular_explica_o_que_mudaria_em_linguagem_humana()
    {
        var (factory, registry, _) = Create();
        registry.Seed(TestHive, TestSubKey, TestValue, 400);

        var action = factory.Create(Spec(), simulationMode: true);
        var explanation = action.SimulateExplanation!();

        Assert.Contains("400", explanation);
        Assert.Contains(TestValue, explanation);
    }

    // =====================================================================
    //  Serviços do Windows (sc.exe + registo coerentes)
    // =====================================================================

    [Fact]
    public void Aplicar_num_servico_escreve_no_registo_e_no_gestor_de_servicos()
    {
        var (factory, registry, runner) = Create();

        const string serviceKey = @"SYSTEM\CurrentControlSet\Services\DiagTrack";
        registry.Seed("HKLM", serviceKey, "Start", 2);

        var spec = new ServiceTweakSpec
        {
            Key = "svc.diagtrack",
            Title = "Telemetria como manual",
            Description = "O serviço só arranca quando for necessário.",
            ServiceName = "DiagTrack",
            OptimizedStart = 3,
        };

        var action = factory.Create(spec, simulationMode: false);
        Assert.True(action.IsAvailable);

        action.Apply!(action);

        Assert.Equal(3, Convert.ToInt32(registry.GetValue("HKLM", serviceKey, "Start")));
        Assert.Contains(runner.Executed, command => command.Contains("sc.exe") && command.Contains("demand"));

        // Backup suficiente para reverter mais tarde, mesmo depois de fechar a aplicação.
        Assert.Equal("DiagTrack", action.Backup[BackupKeys.ServiceName]);
        Assert.Equal("2", action.Backup[BackupKeys.ServiceOriginalStart]);
    }

    [Fact]
    public void Reverter_servico_devolve_o_arranque_automatico()
    {
        var (factory, registry, runner) = Create();

        const string serviceKey = @"SYSTEM\CurrentControlSet\Services\DiagTrack";
        registry.Seed("HKLM", serviceKey, "Start", 2);

        var spec = new ServiceTweakSpec
        {
            Key = "svc.diagtrack",
            Title = "Telemetria como manual",
            Description = "O serviço só arranca quando for necessário.",
            ServiceName = "DiagTrack",
            OptimizedStart = 3,
        };

        var action = factory.Create(spec, simulationMode: false);
        action.Apply!(action);
        registry.Writes.Clear();
        runner.Executed.Clear();

        var revert = action.Revert!(action);

        Assert.True(revert.Success);
        Assert.Equal(2, Convert.ToInt32(registry.GetValue("HKLM", serviceKey, "Start")));
        Assert.Contains(runner.Executed, command => command.Contains("sc.exe") && command.Contains("auto"));
    }

    // =====================================================================
    //  Comandos externos (com comando de reversão obrigatório)
    // =====================================================================

    [Fact]
    public void Aplicar_um_comando_guarda_o_comando_de_reversao()
    {
        var (factory, _, runner) = Create();

        var spec = new CommandTweakSpec
        {
            Key = "net.nagle-off",
            Title = "Desativar Nagle",
            Description = "Reduz a latência em jogos online.",
            Target = @"netsh interface tcp set global",
            FileName = "netsh",
            ApplyArguments = "interface tcp set global autotuninglevel=normal",
            RevertArguments = "interface tcp set global autotuninglevel=normal",
        };

        var action = factory.Create(spec, simulationMode: false);
        action.Apply!(action);

        Assert.Contains(runner.Executed, command => command.StartsWith("netsh"));
        Assert.Equal("netsh interface tcp set global autotuninglevel=normal", action.Backup[BackupKeys.CommandRevert]);

        var revert = action.Revert!(action);
        Assert.True(revert.Success);
    }

    [Fact]
    public void Um_comando_que_falha_propaga_o_erro_para_o_diario()
    {
        var (factory, _, runner) = Create();
        runner.Default = new HLOptimizerPro.Core.Services.Abstractions.CommandResult(1, string.Empty, "parâmetro inválido");

        var spec = new CommandTweakSpec
        {
            Key = "net.exemplo",
            Title = "Alteração de rede",
            Description = "Exemplo de comando que falha.",
            Target = "netsh",
            FileName = "netsh",
            ApplyArguments = "interface tcp set global autotuninglevel=experimental",
            RevertArguments = "interface tcp set global autotuninglevel=normal",
        };

        var action = factory.Create(spec, simulationMode: false);

        // O motor captura a exceção, marca a ação como falhada e continua (ContinueOnError).
        var exception = Assert.Throws<InvalidOperationException>(() => action.Apply!(action));
        Assert.Contains("netsh", exception.Message);
    }
}
