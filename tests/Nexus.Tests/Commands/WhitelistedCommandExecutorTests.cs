using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Domain.Ports;
using Nexus.Infrastructure.Commands;
using Nexus.Tests.Fakes;
using Xunit;

namespace Nexus.Tests.Commands;

/// <summary>
/// Spec §2.2/§7: "nenhum comando de sistema é executado sem estar
/// previamente autorizado numa lista explícita". O executor é testado com um
/// IProcessRunner fake — NENHUM processo real é lançado nestes testes.
/// </summary>
public class WhitelistedCommandExecutorTests
{
    private readonly FakeProcessRunner _runner = new();
    private readonly WhitelistedCommandExecutor _executor =
        new(_runner, NullLogger<WhitelistedCommandExecutor>.Instance);

    [Fact]
    public async Task ScQuery_IsAllowed()
    {
        var result = await _executor.RunAsync(new CommandSpec("sc.exe", "query Spooler"));

        Assert.True(result.WasWhitelisted);
        Assert.True(result.Succeeded);
        Assert.Single(_runner.Calls);
        Assert.Equal("sc.exe", _runner.Calls[0].FileName);
        Assert.Equal("query Spooler", _runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task ScStopNamedService_IsAllowed()
    {
        var result = await _executor.RunAsync(new CommandSpec("sc.exe", "stop Fax"));

        Assert.True(result.WasWhitelisted);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ScChangeStartType_IsAllowed()
    {
        var result = await _executor.RunAsync(new CommandSpec("sc.exe", "change SysMain start=demand"));

        Assert.True(result.WasWhitelisted);
    }

    [Fact]
    public async Task UnknownBinary_IsRejected()
    {
        var result = await _executor.RunAsync(new CommandSpec("powershell.exe", "-c Get-Process"));

        Assert.False(result.WasWhitelisted);
        Assert.False(result.Succeeded);
        Assert.Contains("whitelist", result.RejectionReason);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task ShellMetacharacters_AreRejected_EvenForWhitelistedBinary()
    {
        var result = await _executor.RunAsync(new CommandSpec("sc.exe", "stop Fax & del C:\\Windows"));

        Assert.False(result.WasWhitelisted);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task PipedArguments_AreRejected()
    {
        var result = await _executor.RunAsync(new CommandSpec("sc.exe", "query Spooler | echo pwned"));

        Assert.False(result.WasWhitelisted);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task ScVerbNotInPattern_IsRejected()
    {
        // "delete" não é um verbo autorizado pela whitelist.
        var result = await _executor.RunAsync(new CommandSpec("sc.exe", "delete Spooler"));

        Assert.False(result.WasWhitelisted);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task EmptyArguments_ForSysteminfo_IsAllowed()
    {
        var result = await _executor.RunAsync(new CommandSpec("systeminfo", string.Empty));

        Assert.True(result.WasWhitelisted);
    }

    [Fact]
    public async Task ArgumentsWithTrailingMeta_AreRejected()
    {
        var result = await _executor.RunAsync(new CommandSpec("sc.exe", "query Spooler; whoami"));

        Assert.False(result.WasWhitelisted);
    }

    [Fact]
    public async Task NonZeroExitCode_IsReportedAsNotSucceeded()
    {
        _runner.Result = new(-1073741510, string.Empty, "Falha", false);

        var result = await _executor.RunAsync(new CommandSpec("sc.exe", "query NãoExiste"));

        Assert.True(result.WasWhitelisted);
        Assert.False(result.Succeeded);
        Assert.Equal(-1073741510, result.ExitCode);
    }

    [Fact]
    public async Task Timeout_IsForwardedToRunner()
    {
        await _executor.RunAsync(new CommandSpec("sc.exe", "query Spooler"));

        Assert.Equal(TimeSpan.FromSeconds(10), _runner.Calls[0].Timeout);
    }

    [Fact]
    public async Task EmptyFileName_IsRejected()
    {
        var result = await _executor.RunAsync(new CommandSpec(string.Empty, "query Spooler"));

        Assert.False(result.WasWhitelisted);
        Assert.Empty(_runner.Calls);
    }
}
